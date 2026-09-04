using System.Data;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryLedgerWriter(
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    internal async Task<decimal> PostAsync(
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

            SET @QuantityChange=CAST(@QuantityChange*@InventoryFactor AS DECIMAL(19,6));
            IF @SpecifiedUnitCost IS NOT NULL
              SET @SpecifiedUnitCost=CAST(@SpecifiedUnitCost/@InventoryFactor AS DECIMAL(19,6));

            DECLARE @ManageStock BIT;
            DECLARE @TenantId UNIQUEIDENTIFIER;
            DECLARE @SharesPrices BIT;
            SELECT @TenantId=TenantId,@SharesPrices=SharesProductPrices
            FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND IsActive=1;

            SELECT @ManageStock=p.ManageStock
            FROM dbo.Products p WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Warehouses w WITH(UPDLOCK,HOLDLOCK)
              ON w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId
            WHERE p.ProductId=@ResolvedProductId
              AND (p.TenantId=@TenantId OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId))
              AND p.IsActive=1;

            IF @ManageStock IS NULL
              THROW 51600,'The inventory product or warehouse is outside the business.',1;
            IF @ManageStock=0
            BEGIN
              SELECT CAST(0 AS DECIMAL(19,6));
              RETURN;
            END;

            DECLARE @Exists BIT=0;
            DECLARE @QuantityBefore DECIMAL(19,6)=0;
            DECLARE @AverageBefore DECIMAL(19,6)=0;
            DECLARE @ValueBefore DECIMAL(19,4)=0;
            SELECT @Exists=1,@QuantityBefore=QuantityOnHand,
                   @AverageBefore=AverageUnitCost,@ValueBefore=InventoryValue
            FROM dbo.InventoryBalances WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
              AND ProductId=@ResolvedProductId;

            DECLARE @PoolQuantityBefore DECIMAL(19,6)=0;
            DECLARE @PoolValueBefore DECIMAL(19,4)=0;
            SELECT @PoolQuantityBefore=COALESCE(SUM(balance.QuantityOnHand),0),
                   @PoolValueBefore=COALESCE(SUM(balance.InventoryValue),0)
            FROM dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses poolBusiness WITH(UPDLOCK,HOLDLOCK)
              ON poolBusiness.BusinessId=balance.BusinessId
            WHERE balance.ProductId=@ResolvedProductId
              AND ((@SharesPrices=1 AND poolBusiness.TenantId=@TenantId
                    AND poolBusiness.SharesProductPrices=1 AND poolBusiness.IsActive=1)
                OR (@SharesPrices=0 AND balance.BusinessId=@BusinessId));

            DECLARE @PoolAverageBefore DECIMAL(19,6)=CASE
              WHEN @SharesPrices=0 THEN @AverageBefore
              WHEN @PoolQuantityBefore<>0 AND @PoolValueBefore/@PoolQuantityBefore>=0
                THEN CAST(@PoolValueBefore/@PoolQuantityBefore AS DECIMAL(19,6))
              ELSE @AverageBefore END;

            DECLARE @QuantityAfter DECIMAL(19,6)=
              CAST(@QuantityBefore+@QuantityChange AS DECIMAL(19,6));

            DECLARE @RecognizedUnitCost DECIMAL(19,6);
            DECLARE @ValueChange DECIMAL(19,4);
            DECLARE @ValueAfter DECIMAL(19,4);
            DECLARE @AverageAfter DECIMAL(19,6);
            DECLARE @PoolQuantityAfter DECIMAL(19,6)=CAST(@PoolQuantityBefore+@QuantityChange AS DECIMAL(19,6));

            IF @ValuationMode=N'AverageCost'
            BEGIN
              SET @RecognizedUnitCost=@PoolAverageBefore;
              SET @ValueChange=CAST(@QuantityChange*@RecognizedUnitCost AS DECIMAL(19,4));
              SET @AverageAfter=@PoolAverageBefore;
              SET @ValueAfter=CAST(@QuantityAfter*@AverageAfter AS DECIMAL(19,4));
            END
            ELSE IF @ValuationMode=N'WeightedAverageReceipt'
            BEGIN
              IF @QuantityChange<=0 OR @SpecifiedUnitCost IS NULL
                THROW 51602,'A weighted-average receipt requires positive quantity and unit cost.',1;
              SET @RecognizedUnitCost=@SpecifiedUnitCost;
              SET @ValueChange=CAST(@QuantityChange*@RecognizedUnitCost AS DECIMAL(19,4));
              SET @AverageAfter=CASE
                WHEN @PoolQuantityBefore<=0 THEN @SpecifiedUnitCost
                ELSE CAST((CASE WHEN @PoolValueBefore<0 THEN 0 ELSE @PoolValueBefore END+@ValueChange)
                     /@PoolQuantityAfter AS DECIMAL(19,6)) END;
              SET @ValueAfter=CAST(@QuantityAfter*@AverageAfter AS DECIMAL(19,4));
            END
            ELSE IF @ValuationMode=N'SpecifiedCostIssue'
            BEGIN
              IF @QuantityChange>=0 OR @SpecifiedUnitCost IS NULL
                THROW 51603,'A specified-cost issue requires negative quantity and unit cost.',1;
              SET @RecognizedUnitCost=@SpecifiedUnitCost;
              SET @ValueChange=CAST(@QuantityChange*@RecognizedUnitCost AS DECIMAL(19,4));
              SET @ValueAfter=CASE WHEN @QuantityAfter=0 THEN 0
                ELSE CAST(@ValueBefore+@ValueChange AS DECIMAL(19,4)) END;
              SET @AverageAfter=CASE WHEN @QuantityAfter=0 THEN 0
                ELSE CAST(@ValueAfter/@QuantityAfter AS DECIMAL(19,6)) END;
            END
            ELSE
              THROW 51605,'The inventory valuation mode is not supported.',1;

            IF @AverageAfter<0
              THROW 51607,'The inventory average cost cannot become negative.',1;

            IF @Exists=1
              UPDATE dbo.InventoryBalances
              SET QuantityOnHand=@QuantityAfter,AverageUnitCost=@AverageAfter,
                  InventoryValue=@ValueAfter,LastProcessingSequence=@Sequence,UpdatedAt=@Now
              WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
                AND ProductId=@ResolvedProductId;
            ELSE
              INSERT dbo.InventoryBalances
                (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                 InventoryValue,LastProcessingSequence,UpdatedAt)
              VALUES(@BusinessId,@WarehouseId,@ResolvedProductId,@QuantityAfter,
                     @AverageAfter,@ValueAfter,@Sequence,@Now);

            -- A shared-price group has one valuation cost, while quantities remain
            -- strictly per warehouse. Revalue every existing balance in the pool
            -- without changing its physical quantity.
            UPDATE balance
            SET AverageUnitCost=@AverageAfter,
                InventoryValue=CAST(balance.QuantityOnHand*@AverageAfter AS DECIMAL(19,4)),
                UpdatedAt=@Now
            FROM dbo.InventoryBalances balance
            INNER JOIN dbo.Businesses poolBusiness ON poolBusiness.BusinessId=balance.BusinessId
            WHERE balance.ProductId=@ResolvedProductId
              AND ((@SharesPrices=1 AND poolBusiness.TenantId=@TenantId
                    AND poolBusiness.SharesProductPrices=1 AND poolBusiness.IsActive=1)
                OR (@SharesPrices=0 AND balance.BusinessId=@BusinessId));

            INSERT dbo.InventoryMovements
              (InventoryMovementId,BusinessId,WarehouseId,DocumentId,DocumentType,
               LineNumber,ProductId,MovementType,QuantityChange,ProcessingSequence,
               QuantityBefore,QuantityAfter,AverageUnitCostBefore,AverageUnitCostAfter,
               RecognizedUnitCost,ValueChange,OccurredAt,PostedAt,CreatedAt)
            VALUES(@MovementId,@BusinessId,@WarehouseId,@DocumentId,@DocumentType,
               @LineNumber,@ResolvedProductId,@MovementType,@QuantityChange,@Sequence,
               @QuantityBefore,@QuantityAfter,@PoolAverageBefore,@AverageAfter,
               @RecognizedUnitCost,@ValueChange,@OccurredAt,@Now,@Now);

            IF @AverageAfter<>@PoolAverageBefore
            BEGIN
              DECLARE @CatalogChanges TABLE(
                BusinessId UNIQUEIDENTIFIER NOT NULL,
                CatalogChangeId BIGINT NOT NULL);
              INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @CatalogChanges
              SELECT target.BusinessId,@ResolvedProductId,N'Upsert',@Now
              FROM dbo.Businesses target
              WHERE target.IsActive=1
                AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1)
                  OR (@SharesPrices=0 AND target.BusinessId=@BusinessId))
                AND EXISTS(
                  SELECT 1 FROM dbo.ProductPrices price
                  WHERE price.BusinessId=target.BusinessId
                    AND price.ProductId=@ResolvedProductId AND price.IsActive=1);
              INSERT dbo.PosSynchronizationOutboxMessages(
                NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
              SELECT NEWID(),BusinessId,N'Catalog',CatalogChangeId,@Now
              FROM @CatalogChanges;
            END;
            SELECT @RecognizedUnitCost;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MovementId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", posting.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", posting.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", posting.ProductId);
        command.Parameters.AddWithValue("@DocumentId", posting.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", posting.DocumentType);
        command.Parameters.AddWithValue("@LineNumber", posting.LineNumber);
        command.Parameters.AddWithValue("@MovementType", posting.MovementType);
        AddDecimal(command, "@QuantityChange", posting.QuantityChange, 19, 6);
        var cost = command.Parameters.Add("@SpecifiedUnitCost", SqlDbType.Decimal);
        cost.Precision = 19;
        cost.Scale = 6;
        cost.Value = (object?)posting.SpecifiedUnitCost ?? DBNull.Value;
        command.Parameters.AddWithValue("@ValuationMode", posting.ValuationMode);
        command.Parameters.AddWithValue("@Sequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@OccurredAt", posting.OccurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    internal async Task WriteCalculatedAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        CalculatedInventoryLedgerPosting posting,
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
                THROW 51606,'The inventory balance could not be updated.',1;
            END
            ELSE
              INSERT dbo.InventoryBalances
                (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                 InventoryValue,LastProcessingSequence,UpdatedAt)
              VALUES(@BusinessId,@WarehouseId,@ProductId,@QuantityAfter,@AverageAfter,
                     @ValueAfter,@Sequence,@Now);

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
              DECLARE @CatalogChange TABLE(CatalogChangeId BIGINT NOT NULL);
              INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                OUTPUT inserted.CatalogChangeId INTO @CatalogChange
              SELECT @BusinessId,@ProductId,N'Upsert',@Now
              WHERE EXISTS(
                SELECT 1 FROM dbo.ProductPrices price
                WHERE price.BusinessId=@BusinessId AND price.ProductId=@ProductId
                  AND price.IsActive=1);
              INSERT dbo.PosSynchronizationOutboxMessages(
                NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
              SELECT NEWID(),@BusinessId,N'Catalog',CatalogChangeId,@Now
              FROM @CatalogChange;
            END;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MovementId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", posting.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", posting.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", posting.ProductId);
        command.Parameters.AddWithValue("@DocumentId", posting.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", posting.DocumentType);
        command.Parameters.AddWithValue("@LineNumber", posting.LineNumber);
        command.Parameters.AddWithValue("@MovementType", posting.MovementType);
        command.Parameters.AddWithValue("@BalanceExists", posting.BalanceExists);
        AddDecimal(command, "@QuantityChange", posting.QuantityChange, 19, 6);
        AddDecimal(command, "@QuantityBefore", posting.QuantityBefore, 19, 6);
        AddDecimal(command, "@QuantityAfter", posting.QuantityAfter, 19, 6);
        AddDecimal(command, "@AverageBefore", posting.AverageUnitCostBefore, 19, 6);
        AddDecimal(command, "@AverageAfter", posting.AverageUnitCostAfter, 19, 6);
        AddDecimal(command, "@RecognizedUnitCost", posting.RecognizedUnitCost, 19, 6);
        AddDecimal(command, "@ValueChange", posting.ValueChange, 19, 4);
        AddDecimal(command, "@ValueAfter", posting.InventoryValueAfter, 19, 4);
        command.Parameters.AddWithValue("@Sequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@OccurredAt", posting.OccurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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
}

public static class InventoryValuationModes
{
    public const string AverageCost = "AverageCost";
    public const string WeightedAverageReceipt = "WeightedAverageReceipt";
    public const string SpecifiedCostIssue = "SpecifiedCostIssue";
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
    string ValuationMode,
    DateTimeOffset OccurredAt);

public sealed record CalculatedInventoryLedgerPosting(
    Guid BusinessId,
    Guid WarehouseId,
    Guid ProductId,
    Guid DocumentId,
    string DocumentType,
    int LineNumber,
    string MovementType,
    bool BalanceExists,
    decimal QuantityChange,
    decimal QuantityBefore,
    decimal QuantityAfter,
    decimal AverageUnitCostBefore,
    decimal AverageUnitCostAfter,
    decimal RecognizedUnitCost,
    decimal ValueChange,
    decimal InventoryValueAfter,
    DateTimeOffset OccurredAt);
