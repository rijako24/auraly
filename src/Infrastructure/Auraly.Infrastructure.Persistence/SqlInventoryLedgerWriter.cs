using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlInventoryLedgerWriter(IAuralyIdGenerator ids, TimeProvider timeProvider)
{
    internal async Task<InventoryLedgerPostingResult> PostAsync(
        SqlDocumentProcessingSessionAccessor.Session session, InventoryLedgerPosting posting,
        CancellationToken cancellationToken) =>
        (await PostBatchAsync(session, [posting], cancellationToken))[0];

    // One document is valued in its original movement order. Reads and writes
    // are set based; InventoryValuationCalculator remains the only cost policy.
    internal async Task<IReadOnlyList<InventoryLedgerPostingResult>> PostBatchAsync(
        SqlDocumentProcessingSessionAccessor.Session session, IReadOnlyList<InventoryLedgerPosting> postings,
        CancellationToken cancellationToken)
    {
        if (postings.Count == 0) return [];
        var scope = postings[0];
        if (scope.BusinessId != session.Context.BusinessId.Value ||
            scope.DocumentId != session.Context.DocumentId.Value ||
            scope.DocumentType != session.Context.DocumentType)
            throw new InvalidOperationException("The inventory batch does not belong to the active document-processing session.");
        if (postings.Any(posting => posting.BusinessId != scope.BusinessId ||
                posting.DocumentId != scope.DocumentId ||
                posting.DocumentType != scope.DocumentType) ||
            postings.Select(posting => (posting.LineNumber, posting.MovementType)).Distinct().Count() != postings.Count)
            throw new InvalidOperationException("An inventory batch must contain distinct movements of one business and document.");
        if (postings.Where((posting, position) => posting.UnitCostSourcePosition is { } source &&
                (source < 0 || source >= position || posting.SpecifiedUnitCost is not null ||
                    posting.ValuationMode == InventoryValuationMode.AverageCost)).Any())
            throw new InvalidOperationException("An inventory cost dependency must identify an earlier valued movement.");
        if (postings.Where((posting, position) => posting.OriginSourcePosition is { } source &&
                (source < 0 || source >= position || posting.QuantityChange <= 0m ||
                    postings[source].QuantityChange >= 0m || posting.OriginReference is not null)).Any())
            throw new InvalidOperationException("A transferred origin must identify an earlier issue in the same inventory batch.");
        var loaded = await LoadAsync(session, postings, cancellationToken);
        var targetsByPosition = loaded.Targets.ToDictionary(target => target.Position);
        if (loaded.Targets.Any(target => postings[target.Position].UnitCostSourcePosition is { } source &&
                (targetsByPosition[source].ProductId != target.ProductId || !targetsByPosition[source].ManageStock)))
            throw new InvalidOperationException("An inventory cost dependency must use the same stock product.");
        if (loaded.Targets.Any(target => postings[target.Position].OriginSourcePosition is { } source &&
                (targetsByPosition[source].ProductId != target.ProductId || !targetsByPosition[source].ManageStock)))
            throw new InvalidOperationException("Transferred origins must use the same stock product.");
        var balances = loaded.Balances.ToDictionary(balance => (balance.BusinessId, balance.WarehouseId, balance.ProductId));
        // AVE keeps the damaged quantity at zero book value. It cannot join the
        // sellable cost pool, including when the source balance is negative.
        var pools = loaded.Balances.Where(balance => !balance.IsDamageWarehouse)
            .GroupBy(balance => balance.ProductId)
            .ToDictionary(group => group.Key, group => group.Select(balance => (balance.BusinessId, balance.WarehouseId, balance.ProductId)).ToList());
        var movements = new List<ValuedMovement>(postings.Count);
        var contexts = new CostPoolContexts(loaded, scope.BusinessId, session.ProcessingSequence, ids);
        var origins = new OriginTracking(loaded, scope.BusinessId, session.ProcessingSequence, timeProvider.GetUtcNow(), ids);
        var results = new InventoryLedgerPostingResult[postings.Count];
        var changedProducts = new HashSet<Guid>();
        foreach (var target in loaded.Targets.OrderBy(target => target.Position))
        {
            var posting = postings[target.Position];
            if (!target.ManageStock)
            {
                if (posting.ValuationMode == InventoryValuationMode.ValueAdjustment)
                    throw new InvalidOperationException("Only stock-managed products can receive an inventory value adjustment.");
                results[target.Position] = InventoryLedgerPostingResult.NotManaged;
                continue;
            }
            var key = (scope.BusinessId, posting.WarehouseId, target.ProductId);
            var damageReceipt = posting.MovementType == "DamageWarehouseIn";
            if (!balances.TryGetValue(key, out var balance))
            {
                balance = new(scope.BusinessId, posting.WarehouseId, target.ProductId, 0, 0, 0,
                    false, false, damageReceipt);
                balances.Add(key, balance);
                if (!damageReceipt)
                {
                    if (!pools.TryGetValue(target.ProductId, out var keys)) pools[target.ProductId] = keys = [];
                    keys.Add(key);
                }
            }
            if (damageReceipt != balance.IsDamageWarehouse)
                throw new InvalidOperationException("The damaged inventory warehouse is outside its cost pool.");
            if (damageReceipt && posting.ValuationMode == InventoryValuationMode.ValueAdjustment)
                throw new InvalidOperationException("Damaged inventory cannot receive a sellable inventory value adjustment.");
            List<(Guid BusinessId, Guid WarehouseId, Guid ProductId)> poolKeys =
                damageReceipt ? [key] : pools[target.ProductId];
            var poolQuantity = poolKeys.Sum(poolKey => balances[poolKey].QuantityOnHand);
            var poolValue = poolKeys.Sum(poolKey => balances[poolKey].InventoryValue);
            var epochId = contexts.Resolve(target.ProductId, damageReceipt, poolQuantity);
            var lastKnownCost = poolKeys.Select(poolKey => balances[poolKey].AverageUnitCost)
                .FirstOrDefault(cost => cost > 0m);
            var initialCost = lastKnownCost > 0m ? lastKnownCost : target.InitialUnitCost;
            var quantityChange = posting.ValuationMode == InventoryValuationMode.ValueAdjustment
                ? posting.QuantityChange
                : decimal.Round(posting.QuantityChange * target.InventoryFactor, 6, MidpointRounding.AwayFromZero);
            decimal? specifiedCost = posting.UnitCostSourcePosition is { } costSource
                ? results[costSource].RecognizedUnitCost
                : posting.SpecifiedUnitCost is null ? null :
                decimal.Round(posting.SpecifiedUnitCost.Value / target.InventoryFactor, 6, MidpointRounding.AwayFromZero);
            var valuation = InventoryValuationCalculator.Calculate(
                new(balance.QuantityOnHand, initialCost, poolQuantity, poolValue),
                quantityChange, specifiedCost, posting.ValuationMode, posting.ValueAdjustmentAmount);
            if (posting.MovementType == "Sale" && valuation.RecognizedUnitCost <= 0m)
                throw new InvalidOperationException(
                    "El producto inventariable no tiene una valorización contable positiva. Registra o corrige su costo antes de venderlo.");
            var movementId = ids.NewId();
            balances[key] = origins.Apply(target.Position, posting, balance,
                balance with { QuantityOnHand = valuation.QuantityAfter, IsTarget = true,
                    LastProcessingSequence = session.ProcessingSequence }, movementId, quantityChange);
            foreach (var poolKey in poolKeys)
            {
                var current = balances[poolKey];
                balances[poolKey] = current with { AverageUnitCost = valuation.AverageUnitCostAfter,
                    InventoryValue = decimal.Round(current.QuantityOnHand * valuation.AverageUnitCostAfter, 4, MidpointRounding.AwayFromZero) };
            }
            // Individual warehouse values are rounded before persistence. Use
            // their exact aggregate delta so the movement reconciles to the
            // stored balances even when a shared pool has fractional stock.
            var poolValueAfter = poolKeys.Sum(poolKey => balances[poolKey].InventoryValue);
            var bookValueChange = poolValueAfter - poolValue;
            movements.Add(new(movementId, posting.WarehouseId, posting.LineNumber, target.ProductId, posting.MovementType,
                quantityChange, balance.QuantityOnHand, valuation.QuantityAfter, valuation.AverageUnitCostBefore,
                valuation.AverageUnitCostAfter, valuation.RecognizedUnitCost, bookValueChange, posting.OccurredAt,
                posting.ValuationMode.ToString(), damageReceipt ? "DamageWarehouse" : loaded.SharesPrices ? "SharedTenant" : "Business",
                poolQuantity, poolKeys.Sum(poolKey => balances[poolKey].QuantityOnHand), poolValue, poolValueAfter, epochId));
            if (valuation.AverageUnitCostAfter != valuation.AverageUnitCostBefore) changedProducts.Add(target.ProductId);
            results[target.Position] = new(valuation.QuantityAfter, valuation.AverageUnitCostAfter,
                valuation.InventoryValueAfter, valuation.RecognizedUnitCost, bookValueChange);
        }
        if (movements.Count > 0)
            await PersistAsync(session, scope, loaded, balances.Values.ToArray(), movements, changedProducts, contexts, origins, cancellationToken);
        return results;
    }

    private static async Task<LoadedState> LoadAsync(SqlDocumentProcessingSessionAccessor.Session session,
        IReadOnlyList<InventoryLedgerPosting> postings, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            DECLARE @TenantId uniqueidentifier,@SharesPrices bit;
            SELECT @TenantId=TenantId,@SharesPrices=SharesProductPrices
            FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND TenantId=@ContextTenantId AND IsActive=1;
            IF @TenantId IS NULL OR EXISTS(SELECT 1 FROM OPENJSON(@Postings) WITH(WarehouseId uniqueidentifier) input
              WHERE NOT EXISTS(SELECT 1 FROM dbo.Warehouses WITH(UPDLOCK,HOLDLOCK)
                WHERE WarehouseId=input.WarehouseId AND BusinessId=@BusinessId))
              THROW 51600,'The inventory product or warehouse is outside the business.',1;
            DECLARE @Targets TABLE(Position int PRIMARY KEY,WarehouseId uniqueidentifier,ProductId uniqueidentifier,
              InventoryFactor decimal(19,6),ManageStock bit,InitialUnitCost decimal(19,6));
            INSERT @Targets
            SELECT input.Position,input.WarehouseId,COALESCE(link.ParentProductId,input.ProductId),COALESCE(link.InventoryFactor,1),
              product.ManageStock,COALESCE(price.CostBasisAmount,0)
            FROM OPENJSON(@Postings) WITH(Position int,WarehouseId uniqueidentifier,ProductId uniqueidentifier) input
            LEFT JOIN dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK) ON link.TenantId=@TenantId
              AND link.ChildProductId=input.ProductId AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.Products product WITH(UPDLOCK,HOLDLOCK) ON product.ProductId=COALESCE(link.ParentProductId,input.ProductId)
              AND product.TenantId=@TenantId
            LEFT JOIN dbo.ProductPrices price WITH(UPDLOCK,HOLDLOCK) ON price.BusinessId=@BusinessId
              AND price.ProductId=product.ProductId AND price.IsActive=1;
            IF EXISTS(SELECT 1 FROM @Targets WHERE ManageStock IS NULL)
              THROW 51600,'The inventory product or warehouse is outside the business.',1;
            -- Lock both existing and missing target balances before reading the cost pools.
            SELECT target.Position,target.ProductId,target.InventoryFactor,target.ManageStock,target.InitialUnitCost,
              balance.ProductId LockedProductId
            FROM @Targets target LEFT JOIN dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=target.WarehouseId AND balance.ProductId=target.ProductId
            ORDER BY target.WarehouseId, target.ProductId, target.Position;
            SELECT @TenantId,@SharesPrices;
            DECLARE @Members TABLE(BusinessId uniqueidentifier PRIMARY KEY);
            INSERT @Members SELECT BusinessId FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
            WHERE (@SharesPrices=0 AND BusinessId=@BusinessId)
              OR (@SharesPrices=1 AND TenantId=@TenantId AND SharesProductPrices=1 AND IsActive=1);
            SELECT balance.BusinessId,balance.WarehouseId,balance.ProductId,balance.QuantityOnHand,
              balance.AverageUnitCost,balance.InventoryValue,
              CONVERT(bit,CASE WHEN warehouse.IsSystem=1 AND warehouse.Code=N'AVE' THEN 1 ELSE 0 END),
              balance.LastProcessingSequence,balance.OriginContextId,balance.OriginSnapshotSequence
            FROM dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
            JOIN @Members member ON member.BusinessId=balance.BusinessId
            JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=balance.WarehouseId
            WHERE EXISTS(SELECT 1 FROM @Targets target WHERE target.ProductId=balance.ProductId AND target.ManageStock=1)
            ORDER BY balance.WarehouseId, balance.ProductId, balance.BusinessId;
            SELECT BusinessId FROM @Members ORDER BY BusinessId;
            SELECT head.BusinessId,head.ProductId,head.IsDamageWarehouse,head.EpochId,head.LastProcessingSequence
            FROM inventory.CostPoolHeads head WITH(UPDLOCK,HOLDLOCK)
            JOIN @Members member ON member.BusinessId=head.BusinessId
            WHERE (head.IsDamageWarehouse=0 OR head.BusinessId=@BusinessId)
              AND EXISTS(SELECT 1 FROM @Targets target WHERE target.ManageStock=1 AND target.ProductId=head.ProductId);
            SELECT DISTINCT epoch.EpochId,epoch.ProductId,epoch.ScopeKind,epoch.IsDamageWarehouse,epoch.MemberBusinessIdsJson
            FROM inventory.CostPoolEpochs epoch
            JOIN inventory.CostPoolHeads head ON head.EpochId=epoch.EpochId
            JOIN @Members member ON member.BusinessId=head.BusinessId
            WHERE epoch.TenantId=@TenantId AND (head.IsDamageWarehouse=0 OR head.BusinessId=@BusinessId)
              AND EXISTS(SELECT 1 FROM @Targets target WHERE target.ManageStock=1 AND target.ProductId=head.ProductId);
            """ + OriginsLoadSql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", postings[0].BusinessId);
        command.Parameters.AddWithValue("@ContextTenantId", session.Context.TenantId.Value);
        command.Parameters.Add("@Postings", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            postings.Select((posting, position) => new { Position = position, posting.WarehouseId, posting.ProductId }));
        command.Parameters.Add("@OriginReferences", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            postings.Select((posting, position) => new { Position = position, Reference = posting.OriginReference })
                .Where(item => item.Reference is not null).Select(item => new { item.Position,
                    item.Reference!.DocumentId,item.Reference.DocumentType,item.Reference.LineNumber,item.Reference.MovementType }));
        await using var reader = await command.ExecuteReaderAsync(token);
        var targets = new List<Target>(postings.Count);
        while (await reader.ReadAsync(token)) targets.Add(new(reader.GetInt32(0), reader.GetGuid(1), reader.GetDecimal(2), reader.GetBoolean(3), reader.GetDecimal(4)));
        if (targets.Count != postings.Count) throw new InvalidOperationException("The complete inventory batch could not be resolved.");
        await reader.NextResultAsync(token); await reader.ReadAsync(token);
        var tenantId = reader.GetGuid(0); var sharesPrices = reader.GetBoolean(1);
        await reader.NextResultAsync(token);
        var balances = new List<Balance>();
        while (await reader.ReadAsync(token)) balances.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
            reader.GetDecimal(3),reader.GetDecimal(4),reader.GetDecimal(5),true,false,
            reader.GetBoolean(6),reader.GetInt64(7),reader.IsDBNull(8)?null:reader.GetGuid(8),reader.IsDBNull(9)?null:reader.GetInt64(9)));
        await reader.NextResultAsync(token);
        var members = new List<Guid>();
        while (await reader.ReadAsync(token)) members.Add(reader.GetGuid(0));
        await reader.NextResultAsync(token);
        var heads = new List<PoolHead>();
        while (await reader.ReadAsync(token)) heads.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetBoolean(2),reader.GetGuid(3),
            reader.GetInt64(4)));
        await reader.NextResultAsync(token);
        var epochs = new List<PoolEpoch>();
        while (await reader.ReadAsync(token)) epochs.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetBoolean(3),
            JsonSerializer.Deserialize<Guid[]>(reader.GetString(4)) ?? throw new InvalidOperationException("The cost pool has no membership snapshot.")));
        var (originPositions,originReferences) = await ReadOriginsAsync(reader,token);
        return new(tenantId,sharesPrices,targets,balances,members,heads,epochs,originPositions,originReferences);
    }

    private async Task PersistAsync(SqlDocumentProcessingSessionAccessor.Session session, InventoryLedgerPosting scope,
        LoadedState loaded, IReadOnlyList<Balance> balances, IReadOnlyList<ValuedMovement> movements,
        IReadOnlySet<Guid> changedProducts, CostPoolContexts contexts, OriginTracking origins, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            DECLARE @Epochs TABLE(EpochId uniqueidentifier PRIMARY KEY,ProductId uniqueidentifier,
              ScopeKind nvarchar(24),IsDamageWarehouse bit,MemberBusinessIdsJson nvarchar(max),InitialQuantity decimal(28,6));
            INSERT @Epochs SELECT * FROM OPENJSON(@EpochsJson) WITH(EpochId uniqueidentifier,ProductId uniqueidentifier,
              ScopeKind nvarchar(24),IsDamageWarehouse bit,MemberBusinessIdsJson nvarchar(max),InitialQuantity decimal(28,6));
            INSERT inventory.CostPoolEpochs(EpochId,TenantId,ProductId,ScopeKind,IsDamageWarehouse,
              MemberBusinessIdsJson,InitialQuantity,SourceDocumentId,SourceDocumentType,CreatedAt)
            SELECT EpochId,@TenantId,ProductId,ScopeKind,IsDamageWarehouse,MemberBusinessIdsJson,
              InitialQuantity,@DocumentId,@DocumentType,@Now FROM @Epochs;
            IF @@ROWCOUNT<>@EpochCount THROW 51606,'The inventory cost contexts were not persisted atomically.',1;
            DECLARE @Heads TABLE(BusinessId uniqueidentifier,ProductId uniqueidentifier,IsDamageWarehouse bit,
              EpochId uniqueidentifier,LastProcessingSequence bigint,Existed bit,PreviousEpochId uniqueidentifier,PreviousSequence bigint,
              PRIMARY KEY(BusinessId,ProductId,IsDamageWarehouse));
            INSERT @Heads SELECT * FROM OPENJSON(@HeadsJson) WITH(BusinessId uniqueidentifier,ProductId uniqueidentifier,
              IsDamageWarehouse bit,EpochId uniqueidentifier,LastProcessingSequence bigint,Existed bit,
              PreviousEpochId uniqueidentifier,PreviousSequence bigint);
            UPDATE head SET EpochId=input.EpochId,LastProcessingSequence=input.LastProcessingSequence
            FROM inventory.CostPoolHeads head JOIN @Heads input ON input.BusinessId=head.BusinessId
              AND input.ProductId=head.ProductId AND input.IsDamageWarehouse=head.IsDamageWarehouse
            WHERE input.Existed=1 AND head.EpochId=input.PreviousEpochId AND head.LastProcessingSequence=input.PreviousSequence;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Heads WHERE Existed=1)
              THROW 51606,'The inventory cost context changed while valuation was applied.',1;
            INSERT inventory.CostPoolHeads(BusinessId,ProductId,IsDamageWarehouse,EpochId,LastProcessingSequence)
            SELECT BusinessId,ProductId,IsDamageWarehouse,EpochId,LastProcessingSequence FROM @Heads WHERE Existed=0;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Heads WHERE Existed=0)
              THROW 51606,'The inventory cost context was not persisted atomically.',1;
            DECLARE @Balances TABLE(BusinessId uniqueidentifier,WarehouseId uniqueidentifier,ProductId uniqueidentifier,
              QuantityOnHand decimal(19,6),AverageUnitCost decimal(19,6),InventoryValue decimal(19,4),
              BalanceExists bit,IsTarget bit,OriginContextId uniqueidentifier,OriginSnapshotSequence bigint,
              PRIMARY KEY(BusinessId,WarehouseId,ProductId));
            INSERT @Balances SELECT * FROM OPENJSON(@BalancesJson) WITH(BusinessId uniqueidentifier,WarehouseId uniqueidentifier,
              ProductId uniqueidentifier,QuantityOnHand decimal(19,6),AverageUnitCost decimal(19,6),InventoryValue decimal(19,4),
              BalanceExists bit,IsTarget bit,OriginContextId uniqueidentifier,OriginSnapshotSequence bigint);
            UPDATE balance SET QuantityOnHand=input.QuantityOnHand,AverageUnitCost=input.AverageUnitCost,
              InventoryValue=input.InventoryValue,UpdatedAt=@Now,
              OriginContextId=input.OriginContextId,OriginSnapshotSequence=input.OriginSnapshotSequence,
              LastProcessingSequence=CASE WHEN input.IsTarget=1 THEN @Sequence ELSE balance.LastProcessingSequence END
            FROM dbo.InventoryBalances balance JOIN @Balances input ON input.BusinessId=balance.BusinessId
              AND input.WarehouseId=balance.WarehouseId AND input.ProductId=balance.ProductId WHERE input.BalanceExists=1;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Balances WHERE BalanceExists=1)
              THROW 51606,'The inventory balances changed while valuation was applied.',1;
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
              InventoryValue,LastProcessingSequence,UpdatedAt,OriginContextId,OriginSnapshotSequence)
            SELECT BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,@Sequence,@Now,OriginContextId,OriginSnapshotSequence
            FROM @Balances WHERE BalanceExists=0;
            INSERT dbo.InventoryMovements(InventoryMovementId,BusinessId,WarehouseId,DocumentId,DocumentType,
              LineNumber,ProductId,MovementType,QuantityChange,ProcessingSequence,QuantityBefore,QuantityAfter,
              AverageUnitCostBefore,AverageUnitCostAfter,RecognizedUnitCost,ValueChange,OccurredAt,PostedAt,CreatedAt,
              ValuationMode,CostPoolScope,PoolQuantityBefore,PoolQuantityAfter,PoolInventoryValueBefore,PoolInventoryValueAfter,CostPoolEpochId)
            SELECT m.MovementId,@BusinessId,m.WarehouseId,@DocumentId,@DocumentType,m.LineNumber,m.ProductId,
              m.MovementType,m.QuantityChange,@Sequence,m.QuantityBefore,m.QuantityAfter,m.AverageBefore,m.AverageAfter,
              m.RecognizedUnitCost,m.ValueChange,m.OccurredAt,@Now,@Now,
              m.ValuationMode,m.CostPoolScope,m.PoolQuantityBefore,m.PoolQuantityAfter,m.PoolInventoryValueBefore,m.PoolInventoryValueAfter,m.CostPoolEpochId
            FROM OPENJSON(@Movements) WITH(MovementId uniqueidentifier,WarehouseId uniqueidentifier,LineNumber int,ProductId uniqueidentifier,
              MovementType nvarchar(64),QuantityChange decimal(19,6),QuantityBefore decimal(19,6),QuantityAfter decimal(19,6),
              AverageBefore decimal(19,6),AverageAfter decimal(19,6),RecognizedUnitCost decimal(19,6),ValueChange decimal(19,4),
              OccurredAt datetimeoffset,ValuationMode nvarchar(32),CostPoolScope nvarchar(24),
              PoolQuantityBefore decimal(28,6),PoolQuantityAfter decimal(28,6),
              PoolInventoryValueBefore decimal(28,4),PoolInventoryValueAfter decimal(28,4),CostPoolEpochId uniqueidentifier) m;
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
            """ + OriginsPersistSql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", scope.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", scope.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", scope.DocumentType);
        command.Parameters.AddWithValue("@TenantId", loaded.TenantId);
        command.Parameters.AddWithValue("@SharesPrices", loaded.SharesPrices);
        command.Parameters.AddWithValue("@Sequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@MovementCount", movements.Count);
        command.Parameters.AddWithValue("@EpochCount", contexts.NewEpochs.Count);
        command.Parameters.Add("@EpochsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(contexts.NewEpochs);
        command.Parameters.Add("@HeadsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(contexts.Assignments);
        command.Parameters.Add("@BalancesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(balances);
        command.Parameters.Add("@Movements", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(movements);
        command.Parameters.Add("@ChangedProducts", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(changedProducts);
        origins.AddParameters(command);
        await command.ExecuteNonQueryAsync(token);
    }

    private sealed record Target(int Position,Guid ProductId,decimal InventoryFactor,bool ManageStock,decimal InitialUnitCost);
    private sealed record Balance(Guid BusinessId,Guid WarehouseId,Guid ProductId,decimal QuantityOnHand,
        decimal AverageUnitCost,decimal InventoryValue,bool BalanceExists,bool IsTarget,
        bool IsDamageWarehouse,long LastProcessingSequence = 0,Guid? OriginContextId = null,long? OriginSnapshotSequence = null);
    private sealed record LoadedState(Guid TenantId,bool SharesPrices,IReadOnlyList<Target> Targets,IReadOnlyList<Balance> Balances,
        IReadOnlyList<Guid> MemberBusinesses,IReadOnlyList<PoolHead> Heads,IReadOnlyList<PoolEpoch> Epochs,
        IReadOnlyList<OriginPosition> OriginPositions,IReadOnlyList<OriginReferenceState> OriginReferences);
    private sealed record ValuedMovement(Guid MovementId,Guid WarehouseId,int LineNumber,Guid ProductId,string MovementType,
        decimal QuantityChange,decimal QuantityBefore,decimal QuantityAfter,decimal AverageBefore,decimal AverageAfter,
        decimal RecognizedUnitCost,decimal ValueChange,DateTimeOffset OccurredAt,
        string ValuationMode,string CostPoolScope,decimal PoolQuantityBefore,decimal PoolQuantityAfter,
        decimal PoolInventoryValueBefore,decimal PoolInventoryValueAfter,Guid CostPoolEpochId);
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
    DateTimeOffset OccurredAt,
    int? UnitCostSourcePosition = null,
    decimal? ValueAdjustmentAmount = null,
    int? OriginSourcePosition = null,
    InventoryOriginReference? OriginReference = null);

// A null type resolves the original sale's canonical type in the same batch
// read, covering both fiscal invoices and non-fiscal sales receipts.
public sealed record InventoryOriginReference(Guid DocumentId,string? DocumentType,int LineNumber,string MovementType);

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
