using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryLedgerWriter(IAuralyIdGenerator ids, TimeProvider timeProvider)
{
    internal async Task<InventoryLedgerPostingResult> PostAsync(
        SqlDocumentProcessingSessionAccessor.Session session, InventoryLedgerPosting posting,
        CancellationToken cancellationToken) =>
        (await PostBatchAsync(session, [posting], cancellationToken))[0];

    // One document/warehouse is valued in its original line order. Reads and writes
    // are set based; InventoryValuationCalculator remains the only cost policy.
    internal async Task<IReadOnlyList<InventoryLedgerPostingResult>> PostBatchAsync(
        SqlDocumentProcessingSessionAccessor.Session session, IReadOnlyList<InventoryLedgerPosting> postings,
        CancellationToken cancellationToken)
    {
        if (postings.Count == 0) return [];
        var scope = postings[0];
        if (postings.Any(posting => posting.BusinessId != scope.BusinessId ||
                posting.WarehouseId != scope.WarehouseId || posting.DocumentId != scope.DocumentId ||
                posting.DocumentType != scope.DocumentType) ||
            postings.Select(posting => posting.LineNumber).Distinct().Count() != postings.Count)
            throw new InvalidOperationException("An inventory batch must contain distinct lines of one document and warehouse.");
        var loaded = await LoadAsync(session, postings, cancellationToken);
        var balances = loaded.Balances.ToDictionary(balance => (balance.BusinessId, balance.WarehouseId, balance.ProductId));
        var pools = loaded.Balances.GroupBy(balance => balance.ProductId)
            .ToDictionary(group => group.Key, group => group.Select(balance => (balance.BusinessId, balance.WarehouseId, balance.ProductId)).ToList());
        var movements = new List<ValuedMovement>(postings.Count);
        var results = new InventoryLedgerPostingResult[postings.Count];
        var changedProducts = new HashSet<Guid>();
        foreach (var target in loaded.Targets.OrderBy(target => target.Position))
        {
            var posting = postings[target.Position];
            if (!target.ManageStock) { results[target.Position] = InventoryLedgerPostingResult.NotManaged; continue; }
            var key = (scope.BusinessId, scope.WarehouseId, target.ProductId);
            if (!balances.TryGetValue(key, out var balance))
            {
                balance = new(scope.BusinessId, scope.WarehouseId, target.ProductId, 0, 0, 0, false, false);
                balances.Add(key, balance);
                if (!pools.TryGetValue(target.ProductId, out var keys)) pools[target.ProductId] = keys = [];
                keys.Add(key);
            }
            var poolKeys = pools[target.ProductId];
            var poolQuantity = poolKeys.Sum(poolKey => balances[poolKey].QuantityOnHand);
            var poolValue = poolKeys.Sum(poolKey => balances[poolKey].InventoryValue);
            var quantityChange = decimal.Round(posting.QuantityChange * target.InventoryFactor, 6, MidpointRounding.AwayFromZero);
            decimal? specifiedCost = posting.SpecifiedUnitCost is null ? null :
                decimal.Round(posting.SpecifiedUnitCost.Value / target.InventoryFactor, 6, MidpointRounding.AwayFromZero);
            var valuation = InventoryValuationCalculator.Calculate(
                new(balance.QuantityOnHand, balance.AverageUnitCost, poolQuantity, poolValue),
                quantityChange, specifiedCost, posting.ValuationMode);
            movements.Add(new(ids.NewId(), posting.LineNumber, target.ProductId, posting.MovementType,
                quantityChange, balance.QuantityOnHand, valuation.QuantityAfter, valuation.AverageUnitCostBefore,
                valuation.AverageUnitCostAfter, valuation.RecognizedUnitCost, valuation.ValueChange, posting.OccurredAt));
            balances[key] = balance with { QuantityOnHand = valuation.QuantityAfter, IsTarget = true };
            foreach (var poolKey in poolKeys)
            {
                var current = balances[poolKey];
                balances[poolKey] = current with { AverageUnitCost = valuation.AverageUnitCostAfter,
                    InventoryValue = decimal.Round(current.QuantityOnHand * valuation.AverageUnitCostAfter, 4, MidpointRounding.AwayFromZero) };
            }
            if (valuation.AverageUnitCostAfter != valuation.AverageUnitCostBefore) changedProducts.Add(target.ProductId);
            results[target.Position] = new(valuation.QuantityAfter, valuation.AverageUnitCostAfter,
                valuation.InventoryValueAfter, valuation.RecognizedUnitCost, valuation.ValueChange);
        }
        if (movements.Count > 0)
            await PersistAsync(session, scope, loaded, balances.Values.ToArray(), movements, changedProducts, cancellationToken);
        return results;
    }

    private static async Task<LoadedState> LoadAsync(SqlDocumentProcessingSessionAccessor.Session session,
        IReadOnlyList<InventoryLedgerPosting> postings, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            DECLARE @TenantId uniqueidentifier,@SharesPrices bit;
            SELECT @TenantId=TenantId,@SharesPrices=SharesProductPrices
            FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId AND IsActive=1;
            IF @TenantId IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.Warehouses WITH(UPDLOCK,HOLDLOCK)
              WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId)
              THROW 51600,'The inventory product or warehouse is outside the business.',1;
            DECLARE @Targets TABLE(Position int PRIMARY KEY,ProductId uniqueidentifier,
              InventoryFactor decimal(19,6),ManageStock bit);
            INSERT @Targets
            SELECT input.Position,COALESCE(link.ParentProductId,input.ProductId),COALESCE(link.InventoryFactor,1),product.ManageStock
            FROM OPENJSON(@Postings) WITH(Position int,ProductId uniqueidentifier) input
            LEFT JOIN dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK) ON link.BusinessId=@BusinessId
              AND link.ChildProductId=input.ProductId AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.Products product WITH(UPDLOCK,HOLDLOCK) ON product.ProductId=COALESCE(link.ParentProductId,input.ProductId)
              AND (product.TenantId=@TenantId OR (product.TenantId IS NULL AND product.BusinessId=@BusinessId)) AND product.IsActive=1;
            IF EXISTS(SELECT 1 FROM @Targets WHERE ManageStock IS NULL)
              THROW 51600,'The inventory product or warehouse is outside the business.',1;
            -- Lock both existing and missing target balances before reading the cost pools.
            SELECT target.Position,target.ProductId,target.InventoryFactor,target.ManageStock,
              balance.ProductId LockedProductId
            FROM @Targets target LEFT JOIN dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId AND balance.ProductId=target.ProductId
            ORDER BY target.ProductId, target.Position;
            SELECT @TenantId,@SharesPrices;
            SELECT balance.BusinessId,balance.WarehouseId,balance.ProductId,balance.QuantityOnHand,
              balance.AverageUnitCost,balance.InventoryValue
            FROM dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses poolBusiness WITH(UPDLOCK,HOLDLOCK) ON poolBusiness.BusinessId=balance.BusinessId
            WHERE EXISTS(SELECT 1 FROM @Targets target WHERE target.ProductId=balance.ProductId AND target.ManageStock=1)
              AND ((@SharesPrices=1 AND poolBusiness.TenantId=@TenantId AND poolBusiness.SharesProductPrices=1 AND poolBusiness.IsActive=1)
                OR (@SharesPrices=0 AND balance.BusinessId=@BusinessId))
            ORDER BY balance.WarehouseId, balance.ProductId, balance.BusinessId;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", postings[0].BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", postings[0].WarehouseId);
        command.Parameters.Add("@Postings", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            postings.Select((posting, position) => new { Position = position, posting.ProductId }));
        await using var reader = await command.ExecuteReaderAsync(token);
        var targets = new List<Target>(postings.Count);
        while (await reader.ReadAsync(token)) targets.Add(new(reader.GetInt32(0), reader.GetGuid(1), reader.GetDecimal(2), reader.GetBoolean(3)));
        if (targets.Count != postings.Count) throw new InvalidOperationException("The complete inventory batch could not be resolved.");
        await reader.NextResultAsync(token); await reader.ReadAsync(token);
        var tenantId = reader.GetGuid(0); var sharesPrices = reader.GetBoolean(1);
        await reader.NextResultAsync(token);
        var balances = new List<Balance>();
        while (await reader.ReadAsync(token)) balances.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
            reader.GetDecimal(3),reader.GetDecimal(4),reader.GetDecimal(5),true,false));
        return new(tenantId,sharesPrices,targets,balances);
    }

    private async Task PersistAsync(SqlDocumentProcessingSessionAccessor.Session session, InventoryLedgerPosting scope,
        LoadedState loaded, IReadOnlyList<Balance> balances, IReadOnlyList<ValuedMovement> movements,
        IReadOnlySet<Guid> changedProducts, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            DECLARE @Balances TABLE(BusinessId uniqueidentifier,WarehouseId uniqueidentifier,ProductId uniqueidentifier,
              QuantityOnHand decimal(19,6),AverageUnitCost decimal(19,6),InventoryValue decimal(19,4),
              BalanceExists bit,IsTarget bit,PRIMARY KEY(BusinessId,WarehouseId,ProductId));
            INSERT @Balances SELECT * FROM OPENJSON(@BalancesJson) WITH(BusinessId uniqueidentifier,WarehouseId uniqueidentifier,
              ProductId uniqueidentifier,QuantityOnHand decimal(19,6),AverageUnitCost decimal(19,6),InventoryValue decimal(19,4),
              BalanceExists bit,IsTarget bit);
            UPDATE balance SET QuantityOnHand=input.QuantityOnHand,AverageUnitCost=input.AverageUnitCost,
              InventoryValue=input.InventoryValue,UpdatedAt=@Now,
              LastProcessingSequence=CASE WHEN input.IsTarget=1 THEN @Sequence ELSE balance.LastProcessingSequence END
            FROM dbo.InventoryBalances balance JOIN @Balances input ON input.BusinessId=balance.BusinessId
              AND input.WarehouseId=balance.WarehouseId AND input.ProductId=balance.ProductId WHERE input.BalanceExists=1;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Balances WHERE BalanceExists=1)
              THROW 51606,'The inventory balances changed while valuation was applied.',1;
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
              InventoryValue,LastProcessingSequence,UpdatedAt)
            SELECT BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,@Sequence,@Now
            FROM @Balances WHERE BalanceExists=0;
            INSERT dbo.InventoryMovements(InventoryMovementId,BusinessId,WarehouseId,DocumentId,DocumentType,
              LineNumber,ProductId,MovementType,QuantityChange,ProcessingSequence,QuantityBefore,QuantityAfter,
              AverageUnitCostBefore,AverageUnitCostAfter,RecognizedUnitCost,ValueChange,OccurredAt,PostedAt,CreatedAt)
            SELECT m.MovementId,@BusinessId,@WarehouseId,@DocumentId,@DocumentType,m.LineNumber,m.ProductId,
              m.MovementType,m.QuantityChange,@Sequence,m.QuantityBefore,m.QuantityAfter,m.AverageBefore,m.AverageAfter,
              m.RecognizedUnitCost,m.ValueChange,m.OccurredAt,@Now,@Now
            FROM OPENJSON(@Movements) WITH(MovementId uniqueidentifier,LineNumber int,ProductId uniqueidentifier,
              MovementType nvarchar(64),QuantityChange decimal(19,6),QuantityBefore decimal(19,6),QuantityAfter decimal(19,6),
              AverageBefore decimal(19,6),AverageAfter decimal(19,6),RecognizedUnitCost decimal(19,6),ValueChange decimal(19,4),
              OccurredAt datetimeoffset) m;
            IF @@ROWCOUNT<>@MovementCount THROW 51606,'The inventory movements were not persisted atomically.',1;
            DECLARE @Changes TABLE(BusinessId uniqueidentifier,CatalogChangeId bigint);
            INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
              OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @Changes
            SELECT business.BusinessId,input.ProductId,N'Upsert',@Now
            FROM OPENJSON(@ChangedProducts) WITH(ProductId uniqueidentifier '$') input
            CROSS JOIN dbo.Businesses business
            WHERE business.IsActive=1 AND ((@SharesPrices=1 AND business.TenantId=@TenantId AND business.SharesProductPrices=1)
              OR (@SharesPrices=0 AND business.BusinessId=@BusinessId))
              AND EXISTS(SELECT 1 FROM dbo.ProductPrices price WHERE price.BusinessId=business.BusinessId
                AND price.ProductId=input.ProductId AND price.IsActive=1);
            INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            SELECT NEWID(),BusinessId,N'Catalog',MAX(CatalogChangeId),@Now FROM @Changes GROUP BY BusinessId;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", scope.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", scope.WarehouseId);
        command.Parameters.AddWithValue("@DocumentId", scope.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", scope.DocumentType);
        command.Parameters.AddWithValue("@TenantId", loaded.TenantId);
        command.Parameters.AddWithValue("@SharesPrices", loaded.SharesPrices);
        command.Parameters.AddWithValue("@Sequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@MovementCount", movements.Count);
        command.Parameters.Add("@BalancesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(balances);
        command.Parameters.Add("@Movements", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(movements);
        command.Parameters.Add("@ChangedProducts", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(changedProducts);
        await command.ExecuteNonQueryAsync(token);
    }

    private sealed record Target(int Position,Guid ProductId,decimal InventoryFactor,bool ManageStock);
    private sealed record Balance(Guid BusinessId,Guid WarehouseId,Guid ProductId,decimal QuantityOnHand,
        decimal AverageUnitCost,decimal InventoryValue,bool BalanceExists,bool IsTarget);
    private sealed record LoadedState(Guid TenantId,bool SharesPrices,IReadOnlyList<Target> Targets,IReadOnlyList<Balance> Balances);
    private sealed record ValuedMovement(Guid MovementId,int LineNumber,Guid ProductId,string MovementType,
        decimal QuantityChange,decimal QuantityBefore,decimal QuantityAfter,decimal AverageBefore,decimal AverageAfter,
        decimal RecognizedUnitCost,decimal ValueChange,DateTimeOffset OccurredAt);
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
