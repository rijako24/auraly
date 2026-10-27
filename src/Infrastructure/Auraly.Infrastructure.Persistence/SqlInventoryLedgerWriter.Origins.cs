using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlInventoryLedgerWriter
{
    private const string OriginsLoadSql = """

        SELECT position.BusinessId,position.WarehouseId,position.ProductId,position.ContextId,
          position.OriginId,position.SourceMovementId,position.RecordedAt,position.Quantity,
          COALESCE(source.LineNumber,0),COALESCE(source.ProcessingSequence,0)
        FROM (SELECT DISTINCT WarehouseId,ProductId FROM @Targets WHERE ManageStock=1) target
        JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
          AND balance.WarehouseId=target.WarehouseId AND balance.ProductId=target.ProductId
        JOIN inventory.OriginPositions position WITH(UPDLOCK,HOLDLOCK)
          ON position.BusinessId=balance.BusinessId AND position.WarehouseId=balance.WarehouseId
          AND position.ProductId=balance.ProductId AND position.ContextId=balance.OriginContextId
        LEFT JOIN dbo.InventoryMovements source ON source.InventoryMovementId=position.SourceMovementId
        WHERE balance.OriginSnapshotSequence=balance.LastProcessingSequence;
        DECLARE @References TABLE(Position int PRIMARY KEY,InventoryMovementId uniqueidentifier);
        INSERT @References
        SELECT input.Position,movement.InventoryMovementId
        FROM OPENJSON(@OriginReferences) WITH(Position int,DocumentId uniqueidentifier,
          DocumentType nvarchar(64),LineNumber int,MovementType nvarchar(32)) input
        JOIN @Targets target ON target.Position=input.Position
        LEFT JOIN dbo.SalesDocuments sale ON sale.BusinessId=@BusinessId AND sale.DocumentId=input.DocumentId
          AND input.DocumentType IS NULL
        JOIN dbo.InventoryMovements movement WITH(UPDLOCK,HOLDLOCK) ON movement.BusinessId=@BusinessId
          AND movement.DocumentId=input.DocumentId AND movement.DocumentType=COALESCE(input.DocumentType,sale.DocumentType)
          AND movement.LineNumber=input.LineNumber AND movement.MovementType=input.MovementType
          AND movement.ProductId=target.ProductId;
        SELECT reference.Position,reference.InventoryMovementId,origin.OriginId,origin.SourceMovementId,
          origin.RecordedAt,origin.QuantityChange,COALESCE(restored.Quantity,0),
          COALESCE(source.LineNumber,0),COALESCE(source.ProcessingSequence,0)
        FROM @References reference
        LEFT JOIN inventory.MovementOrigins origin WITH(UPDLOCK,HOLDLOCK)
          ON origin.InventoryMovementId=reference.InventoryMovementId
        LEFT JOIN dbo.InventoryMovements source ON source.InventoryMovementId=origin.SourceMovementId
        OUTER APPLY(SELECT SUM(restoration.QuantityChange) Quantity FROM inventory.MovementOrigins restoration WITH(UPDLOCK,HOLDLOCK)
          WHERE restoration.ReferencedMovementId=reference.InventoryMovementId AND restoration.OriginId=origin.OriginId
            AND restoration.QuantityChange>0) restored;
        """;

    private const string OriginsPersistSql = """

        DECLARE @OriginContexts TABLE(WarehouseId uniqueidentifier,ProductId uniqueidentifier,
          ContextId uniqueidentifier,PRIMARY KEY(WarehouseId,ProductId));
        INSERT @OriginContexts SELECT * FROM OPENJSON(@OriginContextsJson)
          WITH(WarehouseId uniqueidentifier,ProductId uniqueidentifier,ContextId uniqueidentifier);
        -- Replace only touched physical warehouse contexts. Older contexts
        -- remain historical when an older writer broke origin coverage.
        DELETE position FROM inventory.OriginPositions position JOIN @OriginContexts context
          ON position.BusinessId=@BusinessId AND position.WarehouseId=context.WarehouseId
          AND position.ProductId=context.ProductId AND position.ContextId=context.ContextId;
        INSERT inventory.OriginPositions(BusinessId,WarehouseId,ProductId,ContextId,OriginId,SourceMovementId,RecordedAt,Quantity)
        SELECT @BusinessId,input.WarehouseId,input.ProductId,input.ContextId,input.OriginId,
          input.SourceMovementId,input.RecordedAt,input.Quantity
        FROM OPENJSON(@OriginPositionsJson) WITH(WarehouseId uniqueidentifier,ProductId uniqueidentifier,
          ContextId uniqueidentifier,OriginId uniqueidentifier,SourceMovementId uniqueidentifier,
          RecordedAt datetimeoffset,Quantity decimal(19,6)) input;
        INSERT inventory.MovementOrigins(InventoryMovementId,OriginId,SourceMovementId,RecordedAt,
          QuantityChange,ReferencedMovementId)
        SELECT * FROM OPENJSON(@MovementOriginsJson) WITH(InventoryMovementId uniqueidentifier,
          OriginId uniqueidentifier,SourceMovementId uniqueidentifier,RecordedAt datetimeoffset,
          QuantityChange decimal(19,6),ReferencedMovementId uniqueidentifier);
        """;

    private sealed record OriginPosition(Guid BusinessId,Guid WarehouseId,Guid ProductId,Guid ContextId,Guid OriginId,Guid? SourceMovementId,
        DateTimeOffset RecordedAt,decimal Quantity,int SourceLineNumber,long SourceProcessingSequence);
    private sealed record MovementOrigin(Guid InventoryMovementId,Guid OriginId,Guid? SourceMovementId,
        DateTimeOffset RecordedAt,decimal QuantityChange,Guid? ReferencedMovementId);
    private sealed record OriginReferenceState(int Position,Guid MovementId,
        IReadOnlyList<InventoryOriginPortion> Portions,IReadOnlyDictionary<Guid,decimal> Restored);

    private static async Task<(IReadOnlyList<OriginPosition>,IReadOnlyList<OriginReferenceState>)> ReadOriginsAsync(
        SqlDataReader reader,CancellationToken token)
    {
        await reader.NextResultAsync(token);
        var positions = new List<OriginPosition>();
        while (await reader.ReadAsync(token)) positions.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
            reader.GetGuid(3),reader.GetGuid(4),reader.IsDBNull(5)?null:reader.GetGuid(5),
            reader.GetFieldValue<DateTimeOffset>(6),reader.GetDecimal(7),reader.GetInt32(8),reader.GetInt64(9)));
        await reader.NextResultAsync(token);
        var references = new Dictionary<int,(Guid Movement,List<InventoryOriginPortion> Portions,Dictionary<Guid,decimal> Restored)>();
        while (await reader.ReadAsync(token))
        {
            var position = reader.GetInt32(0);
            if (!references.TryGetValue(position,out var reference))
                references.Add(position,reference=(reader.GetGuid(1),[],[]));
            if (reader.IsDBNull(2)) continue; // Explicit historical absence, not an inferred receipt.
            var originId = reader.GetGuid(2);
            reference.Portions.Add(new(originId,reader.IsDBNull(3)?null:reader.GetGuid(3),
                reader.GetFieldValue<DateTimeOffset>(4),reader.GetDecimal(5),reader.GetInt32(7),reader.GetInt64(8)));
            reference.Restored.Add(originId,reader.GetDecimal(6));
        }
        return (positions,references.Select(item=>new OriginReferenceState(item.Key,item.Value.Movement,
            item.Value.Portions,item.Value.Restored)).ToArray());
    }

    private sealed class OriginTracking(LoadedState loaded,Guid businessId,long sequence,
        DateTimeOffset recordedAt,IAuralyIdGenerator ids)
    {
        private readonly Dictionary<(Guid Warehouse,Guid Product),(Guid Context,IReadOnlyList<InventoryOriginPortion> Available)> current = [];
        private readonly Dictionary<int,IReadOnlyList<InventoryOriginPortion>> movements = [];
        private readonly Dictionary<(Guid Movement,Guid Origin),decimal> restoredInBatch = [];
        private readonly Dictionary<int,OriginReferenceState> references = loaded.OriginReferences.ToDictionary(reference=>reference.Position);
        private readonly ILookup<(Guid Warehouse,Guid Product,Guid Context),OriginPosition> positions =
            loaded.OriginPositions.Where(item=>item.BusinessId==businessId)
                .ToLookup(item=>(item.WarehouseId,item.ProductId,item.ContextId));
        public List<MovementOrigin> Allocations { get; } = [];

        public Balance Apply(int position,InventoryLedgerPosting posting,Balance before,Balance after,
            Guid movementId,decimal quantityChange)
        {
            if (quantityChange==0m)
            {
                return before.OriginContextId is not null && before.OriginSnapshotSequence==before.LastProcessingSequence
                    ? after with { OriginSnapshotSequence=sequence } : after;
            }
            var key=(posting.WarehouseId,before.ProductId);
            if (!current.TryGetValue(key,out var state))
            {
                if (before.OriginContextId is { } retained && before.OriginSnapshotSequence==before.LastProcessingSequence)
                    state=(retained,positions[(key.WarehouseId,key.ProductId,retained)]
                        .Select(item=>new InventoryOriginPortion(item.OriginId,item.SourceMovementId,item.RecordedAt,
                            item.Quantity,item.SourceLineNumber,item.SourceProcessingSequence)).ToArray());
                else
                    state=(ids.NewId(),before.QuantityOnHand>0m ? [new(ids.NewId(),null,recordedAt,before.QuantityOnHand)] : []);
            }
            var unknown = new InventoryOriginPortion(movementId,null,recordedAt,0m);
            references.TryGetValue(position,out var reference);
            IReadOnlyList<InventoryOriginPortion>? incoming = null;
            if (quantityChange>0m)
            {
                if (posting.OriginSourcePosition is { } source)
                {
                    if (!movements.TryGetValue(source,out var previous) || previous.Any(portion=>portion.Quantity>=0m))
                        throw new InvalidOperationException("A transferred origin must reference an earlier inventory issue.");
                    incoming=previous.Select(portion=>portion with { Quantity=-portion.Quantity }).ToArray();
                }
                else if (reference is { Portions.Count: >0 })
                    incoming=Restore(reference,quantityChange);
                else incoming=[new(movementId,posting.OriginReference is null?movementId:null,recordedAt,quantityChange,
                    posting.OriginReference is null?posting.LineNumber:0,posting.OriginReference is null?sequence:0)];
            }
            var result=InventoryOriginAttribution.Apply(before.QuantityOnHand,quantityChange,state.Available,incoming,unknown);
            current[key]=(state.Context,result.Available);
            movements.Add(position,result.Movement);
            foreach (var portion in result.Movement)
            {
                Allocations.Add(new(movementId,portion.OriginId,portion.SourceMovementId,portion.RecordedAt,
                    portion.Quantity,reference?.MovementId));
                if (reference is not null && portion.Quantity>0m)
                {
                    var returnedKey=(reference.MovementId,portion.OriginId);
                    restoredInBatch[returnedKey]=restoredInBatch.GetValueOrDefault(returnedKey)+portion.Quantity;
                }
            }
            return after with { OriginContextId=state.Context,OriginSnapshotSequence=sequence };
        }

        private IReadOnlyList<InventoryOriginPortion> Restore(OriginReferenceState reference,decimal quantity)
        {
            if (reference.Portions.Any(portion=>portion.Quantity>=0m))
                throw new InvalidOperationException("An inventory return must reference an issue.");
            var result=new List<InventoryOriginPortion>();
            foreach(var portion in InventoryOriginAttribution.OldestFirst(reference.Portions))
            {
                var available=-portion.Quantity-reference.Restored.GetValueOrDefault(portion.OriginId)
                    -restoredInBatch.GetValueOrDefault((reference.MovementId,portion.OriginId));
                if (available<0m) throw new InvalidOperationException("An inventory origin has been returned beyond its issue quantity.");
                var restored=Math.Min(available,quantity);
                if (restored>0m) result.Add(portion with { Quantity=restored });
                quantity-=restored;
                if (quantity==0m) break;
            }
            if (quantity!=0m) throw new InvalidOperationException("The return exceeds the remaining origin quantity of its issue.");
            return result;
        }

        public void AddParameters(SqlCommand command)
        {
            command.Parameters.Add("@OriginContextsJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(
                current.Select(item=>new { WarehouseId=item.Key.Warehouse,ProductId=item.Key.Product,ContextId=item.Value.Context }));
            command.Parameters.Add("@OriginPositionsJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(
                current.SelectMany(item=>item.Value.Available.Select(portion=>new {
                    WarehouseId=item.Key.Warehouse,ProductId=item.Key.Product,ContextId=item.Value.Context,
                    portion.OriginId,portion.SourceMovementId,portion.RecordedAt,portion.Quantity })));
            command.Parameters.Add("@MovementOriginsJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(Allocations);
        }
    }
}
