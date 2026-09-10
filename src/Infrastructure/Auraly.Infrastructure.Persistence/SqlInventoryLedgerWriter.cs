using System.Data;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryLedgerWriter(
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    internal async Task<InventoryLedgerPostingResult> PostAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        InventoryLedgerPosting posting,
        CancellationToken cancellationToken)
    {
        var target = await LoadTargetAsync(session, posting, cancellationToken);
        if (!target.ManageStock)
            return InventoryLedgerPostingResult.NotManaged;

        var pool = await LoadPoolAsync(session, posting.BusinessId, target, cancellationToken);
        var quantityChange = Quantity(posting.QuantityChange * target.InventoryFactor);
        decimal? specifiedUnitCost = posting.SpecifiedUnitCost is null
            ? null
            : UnitCost(posting.SpecifiedUnitCost.Value / target.InventoryFactor);
        var valuation = InventoryValuationCalculator.Calculate(
            new InventoryValuationState(
                target.QuantityOnHand,
                target.AverageUnitCost,
                pool.QuantityOnHand,
                pool.InventoryValue),
            quantityChange,
            specifiedUnitCost,
            posting.ValuationMode);

        await PersistAsync(
            session,
            posting,
            target,
            quantityChange,
            valuation,
            cancellationToken);

        return new InventoryLedgerPostingResult(
            valuation.QuantityAfter,
            valuation.AverageUnitCostAfter,
            valuation.InventoryValueAfter,
            valuation.RecognizedUnitCost,
            valuation.ValueChange);
    }

    private static async Task<InventoryTargetState> LoadTargetAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        InventoryLedgerPosting posting,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @ResolvedProductId UNIQUEIDENTIFIER=@ProductId;
            DECLARE @InventoryFactor DECIMAL(19,6)=1;
            SELECT @ResolvedProductId=l.ParentProductId,@InventoryFactor=l.InventoryFactor
            FROM dbo.ProductLinks l WITH(UPDLOCK,HOLDLOCK)
            WHERE l.BusinessId=@BusinessId AND l.ChildProductId=@ProductId
              AND l.SharesInventory=1 AND l.IsActive=1;

            DECLARE @TenantId UNIQUEIDENTIFIER;
            DECLARE @SharesPrices BIT;
            SELECT @TenantId=TenantId,@SharesPrices=SharesProductPrices
            FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND IsActive=1;

            DECLARE @ManageStock BIT;
            SELECT @ManageStock=p.ManageStock
            FROM dbo.Products p WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Warehouses w WITH(UPDLOCK,HOLDLOCK)
              ON w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId
            WHERE p.ProductId=@ResolvedProductId
              AND (p.TenantId=@TenantId OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId))
              AND p.IsActive=1;

            IF @ManageStock IS NULL
              THROW 51600,'The inventory product or warehouse is outside the business.',1;

            SELECT @ResolvedProductId,@InventoryFactor,@ManageStock,@TenantId,@SharesPrices,
                   COALESCE(balance.QuantityOnHand,0),COALESCE(balance.AverageUnitCost,0),
                   CAST(CASE WHEN balance.BusinessId IS NULL THEN 0 ELSE 1 END AS bit)
            FROM (VALUES(1)) seed(Value)
            LEFT JOIN dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId
             AND balance.ProductId=@ResolvedProductId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", posting.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", posting.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", posting.ProductId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The inventory target could not be loaded.");
        return new InventoryTargetState(
            reader.GetGuid(0),
            reader.GetDecimal(1),
            reader.GetBoolean(2),
            reader.GetGuid(3),
            reader.GetBoolean(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetBoolean(7));
    }

    private static async Task<InventoryPoolState> LoadPoolAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid businessId,
        InventoryTargetState target,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(SUM(balance.QuantityOnHand),0),
                   COALESCE(SUM(balance.InventoryValue),0)
            FROM dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses poolBusiness WITH(UPDLOCK,HOLDLOCK)
              ON poolBusiness.BusinessId=balance.BusinessId
            WHERE balance.ProductId=@ProductId
              AND ((@SharesPrices=1 AND poolBusiness.TenantId=@TenantId
                    AND poolBusiness.SharesProductPrices=1 AND poolBusiness.IsActive=1)
                OR (@SharesPrices=0 AND balance.BusinessId=@BusinessId));
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@ProductId", target.ProductId);
        command.Parameters.AddWithValue("@SharesPrices", target.SharesPrices);
        command.Parameters.AddWithValue("@TenantId", target.TenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The inventory valuation pool could not be loaded.");
        return new InventoryPoolState(reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private async Task PersistAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        InventoryLedgerPosting posting,
        InventoryTargetState target,
        decimal quantityChange,
        InventoryValuationResult valuation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF @BalanceExists=1
            BEGIN
              UPDATE dbo.InventoryBalances
              SET QuantityOnHand=@QuantityAfter,AverageUnitCost=@AverageAfter,
                  InventoryValue=@ValueAfter,LastProcessingSequence=@Sequence,UpdatedAt=@Now
              WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
              IF @@ROWCOUNT<>1
                THROW 51606,'The inventory balance changed while valuation was applied.',1;
            END
            ELSE
              INSERT dbo.InventoryBalances
                (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                 InventoryValue,LastProcessingSequence,UpdatedAt)
              VALUES(@BusinessId,@WarehouseId,@ProductId,@QuantityAfter,@AverageAfter,
                     @ValueAfter,@Sequence,@Now);

            -- Quantities remain warehouse-owned. A shared-price pool has one
            -- canonical average cost, so every existing balance is revalued.
            UPDATE balance
            SET AverageUnitCost=@AverageAfter,
                InventoryValue=CAST(balance.QuantityOnHand*@AverageAfter AS DECIMAL(19,4)),
                UpdatedAt=@Now
            FROM dbo.InventoryBalances balance
            INNER JOIN dbo.Businesses poolBusiness ON poolBusiness.BusinessId=balance.BusinessId
            WHERE balance.ProductId=@ProductId
              AND ((@SharesPrices=1 AND poolBusiness.TenantId=@TenantId
                    AND poolBusiness.SharesProductPrices=1 AND poolBusiness.IsActive=1)
                OR (@SharesPrices=0 AND balance.BusinessId=@BusinessId));

            INSERT dbo.InventoryMovements
              (InventoryMovementId,BusinessId,WarehouseId,DocumentId,DocumentType,
               LineNumber,ProductId,MovementType,QuantityChange,ProcessingSequence,
               QuantityBefore,QuantityAfter,AverageUnitCostBefore,AverageUnitCostAfter,
               RecognizedUnitCost,ValueChange,OccurredAt,PostedAt,CreatedAt)
            VALUES(@MovementId,@BusinessId,@WarehouseId,@DocumentId,@DocumentType,
               @LineNumber,@ProductId,@MovementType,@QuantityChange,@Sequence,
               @QuantityBefore,@QuantityAfter,@AverageBefore,@AverageAfter,
               @RecognizedUnitCost,@ValueChange,@OccurredAt,@Now,@Now);

            IF @AverageAfter<>@AverageBefore
            BEGIN
              DECLARE @CatalogChanges TABLE(
                BusinessId UNIQUEIDENTIFIER NOT NULL,
                CatalogChangeId BIGINT NOT NULL);
              INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @CatalogChanges
              SELECT business.BusinessId,@ProductId,N'Upsert',@Now
              FROM dbo.Businesses business
              WHERE business.IsActive=1
                AND ((@SharesPrices=1 AND business.TenantId=@TenantId AND business.SharesProductPrices=1)
                  OR (@SharesPrices=0 AND business.BusinessId=@BusinessId))
                AND EXISTS(
                  SELECT 1 FROM dbo.ProductPrices price
                  WHERE price.BusinessId=business.BusinessId
                    AND price.ProductId=@ProductId AND price.IsActive=1);
              INSERT dbo.PosSynchronizationOutboxMessages(
                NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
              SELECT NEWID(),BusinessId,N'Catalog',CatalogChangeId,@Now
              FROM @CatalogChanges;
            END;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MovementId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", posting.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", posting.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", target.ProductId);
        command.Parameters.AddWithValue("@DocumentId", posting.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", posting.DocumentType);
        command.Parameters.AddWithValue("@LineNumber", posting.LineNumber);
        command.Parameters.AddWithValue("@MovementType", posting.MovementType);
        command.Parameters.AddWithValue("@BalanceExists", target.BalanceExists);
        command.Parameters.AddWithValue("@SharesPrices", target.SharesPrices);
        command.Parameters.AddWithValue("@TenantId", target.TenantId);
        AddDecimal(command, "@QuantityChange", quantityChange, 19, 6);
        AddDecimal(command, "@QuantityBefore", target.QuantityOnHand, 19, 6);
        AddDecimal(command, "@QuantityAfter", valuation.QuantityAfter, 19, 6);
        AddDecimal(command, "@AverageBefore", valuation.AverageUnitCostBefore, 19, 6);
        AddDecimal(command, "@AverageAfter", valuation.AverageUnitCostAfter, 19, 6);
        AddDecimal(command, "@RecognizedUnitCost", valuation.RecognizedUnitCost, 19, 6);
        AddDecimal(command, "@ValueChange", valuation.ValueChange, 19, 4);
        AddDecimal(command, "@ValueAfter", valuation.InventoryValueAfter, 19, 4);
        command.Parameters.AddWithValue("@Sequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@OccurredAt", posting.OccurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static decimal Quantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static decimal UnitCost(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static void AddDecimal(
        SqlCommand command,
        string name,
        decimal value,
        byte precision,
        byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

    private sealed record InventoryTargetState(
        Guid ProductId,
        decimal InventoryFactor,
        bool ManageStock,
        Guid TenantId,
        bool SharesPrices,
        decimal QuantityOnHand,
        decimal AverageUnitCost,
        bool BalanceExists);

    private sealed record InventoryPoolState(decimal QuantityOnHand, decimal InventoryValue);
}

public sealed record InventoryLedgerPosting(
    Guid BusinessId,
    Guid WarehouseId,
    Guid ProductId,
    Guid DocumentId,
    string DocumentType,
    int LineNumber,
    string MovementType,
    decimal QuantityChange,
    decimal? SpecifiedUnitCost,
    InventoryValuationMode ValuationMode,
    DateTimeOffset OccurredAt);

public sealed record InventoryLedgerPostingResult(
    decimal QuantityAfter,
    decimal AverageUnitCostAfter,
    decimal InventoryValueAfter,
    decimal RecognizedUnitCost,
    decimal ValueChange)
{
    public static InventoryLedgerPostingResult NotManaged { get; } =
        new(0m, 0m, 0m, 0m, 0m);
}
