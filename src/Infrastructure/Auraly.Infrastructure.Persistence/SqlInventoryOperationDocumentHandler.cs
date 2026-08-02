using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Inventory;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryOperationProcessor(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    public async Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken)
    {
        var operation = InventoryOperationContractSerializer.Deserialize(document.Payload);
        if (operation.DocumentId != document.DocumentId.Value || operation.BusinessId != document.BusinessId.Value || operation.TenantId != document.TenantId.Value || operation.DocumentType != document.DocumentType)
            throw new InvalidOperationException("The inventory operation envelope does not match its payload.");
        var session = sessions.Current;
        var balances = await LockBalancesAsync(session, operation, cancellationToken);
        var totalValueChange = operation.DocumentType switch
        {
            InventoryDocumentTypes.StockCount => await ProcessCountAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Adjustment => await ProcessAdjustmentAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Transfer => await ProcessTransferAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Conversion => await ProcessConversionAsync(session, operation, balances, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported inventory operation '{operation.DocumentType}'.")
        };
        await InsertOutboxAsync(session, operation, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, operation, totalValueChange, cancellationToken);
    }

    private static async Task<Dictionary<(Guid Warehouse, Guid Product), BalanceState>> LockBalancesAsync(
        SqlDocumentProcessingSessionAccessor.Session session, InventoryOperationDocumentPayload operation, CancellationToken cancellationToken)
    {
        var keys = operation.Lines.Select(line => (operation.WarehouseId, line.ProductId)).ToList();
        if (operation.DestinationWarehouseId is { } destination)
            keys.AddRange(operation.Lines.Select(line => (destination, line.ProductId)));
        var result = new Dictionary<(Guid, Guid), BalanceState>();
        const string sql = """
            SELECT p.ManageStock,b.QuantityOnHand,b.AverageUnitCost,b.InventoryValue
            FROM dbo.Products p WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Warehouses w WITH(UPDLOCK,HOLDLOCK) ON w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId
            LEFT JOIN dbo.InventoryBalances b WITH(UPDLOCK,HOLDLOCK)
              ON b.BusinessId=@BusinessId AND b.WarehouseId=@WarehouseId AND b.ProductId=p.ProductId
            WHERE p.BusinessId=@BusinessId AND p.ProductId=@ProductId AND p.IsActive=1;
            """;
        foreach (var key in keys.Distinct().OrderBy(key => key.Item1).ThenBy(key => key.ProductId))
        {
            await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
            command.Parameters.AddWithValue("@WarehouseId", key.Item1);
            command.Parameters.AddWithValue("@ProductId", key.ProductId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0))
                throw new InvalidOperationException("An inventory product or warehouse is no longer valid.");
            result[key] = new BalanceState(
                reader.IsDBNull(1) ? 0m : reader.GetDecimal(1),
                reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                !reader.IsDBNull(1));
        }
        return result;
    }

    private async Task<decimal> ProcessCountAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var total = 0m;
        foreach (var line in operation.Lines.OrderBy(line => line.LineNumber))
        {
            var change = InventoryOperationRules.CountAdjustment(line.Quantity, line.SystemQuantityAtBase ?? throw new InvalidOperationException("The stock count base is missing."));
            if (change == 0) { await UpdateLineResultAsync(session, operation.DocumentId, line.LineNumber, balances[(operation.WarehouseId,line.ProductId)].AverageCost, 0m, cancellationToken); continue; }
            total += await ApplyAsync(session, operation, line, operation.WarehouseId, change, null, "StockCountAdjustment", balances, cancellationToken);
        }
        return InventoryOperationRules.Money(total);
    }

    private async Task<decimal> ProcessAdjustmentAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var total = 0m;
        foreach (var line in operation.Lines.OrderBy(line => line.LineNumber))
            total += await ApplyAsync(session, operation, line, operation.WarehouseId, line.Quantity, line.ExplicitUnitCost, "InventoryAdjustment", balances, cancellationToken);
        return InventoryOperationRules.Money(total);
    }

    private async Task<decimal> ProcessTransferAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var destination = operation.DestinationWarehouseId ?? throw new InvalidOperationException("The transfer destination is missing.");
        var total = 0m;
        foreach (var line in operation.Lines.OrderBy(line => line.LineNumber))
        {
            var source = balances[(operation.WarehouseId, line.ProductId)];
            var transferCost = source.AverageCost;
            total += await ApplyAsync(session, operation, line, operation.WarehouseId, -line.Quantity, null, "TransferOut", balances, cancellationToken, false);
            total += await ApplyAsync(session, operation, line, destination, line.Quantity, transferCost, "TransferIn", balances, cancellationToken);
        }
        return InventoryOperationRules.Money(total);
    }

    private async Task<decimal> ProcessConversionAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var inputs = operation.Lines.Where(line => line.Direction == "INPUT").OrderBy(line => line.LineNumber).ToArray();
        var outputs = operation.Lines.Where(line => line.Direction == "OUTPUT").OrderBy(line => line.LineNumber).ToArray();
        var inputCost = 0m;
        foreach (var line in inputs)
            inputCost -= await ApplyAsync(session, operation, line, operation.WarehouseId, -line.Quantity, null, "ConversionInput", balances, cancellationToken);
        var allocations = InventoryOperationRules.AllocateConversionCost(inputCost, outputs.Select(line => (line.Quantity, line.AllocationWeight)).ToArray());
        var total = -inputCost;
        for (var index = 0; index < outputs.Length; index++)
        {
            var unitCost = InventoryOperationRules.Quantity(allocations[index] / outputs[index].Quantity);
            total += await ApplyAsync(session, operation, outputs[index], operation.WarehouseId, outputs[index].Quantity, unitCost, "ConversionOutput", balances, cancellationToken);
        }
        return InventoryOperationRules.Money(total);
    }

    private async Task<decimal> ApplyAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, InventoryOperationLineSnapshot line, Guid warehouseId,
        decimal quantityChange, decimal? inboundUnitCost, string movementType,
        Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken, bool updateLine = true)
    {
        var state = balances[(warehouseId, line.ProductId)];
        var beforeQuantity = state.Quantity;
        var beforeAverage = state.AverageCost;
        decimal valueChange;
        decimal afterQuantity;
        decimal afterAverage;
        decimal afterValue;
        decimal recognizedCost;
        if (quantityChange > 0)
        {
            recognizedCost = inboundUnitCost ?? state.AverageCost;
            var valuation = WeightedAverageCost.ApplyReceipt(state.Quantity, state.Value, quantityChange, recognizedCost);
            afterQuantity = valuation.QuantityAfter; afterAverage = valuation.AverageUnitCostAfter; afterValue = valuation.InventoryValueAfter; valueChange = valuation.ReceiptValue;
        }
        else
        {
            var outgoing = -quantityChange;
            if (state.Quantity < outgoing) throw new InvalidOperationException("The inventory operation would create a negative balance.");
            recognizedCost = state.AverageCost;
            afterQuantity = InventoryOperationRules.Quantity(state.Quantity - outgoing);
            valueChange = -InventoryOperationRules.Money(outgoing * recognizedCost);
            afterValue = afterQuantity == 0 ? 0m : InventoryOperationRules.Money(state.Value + valueChange);
            afterAverage = afterQuantity == 0 ? 0m : state.AverageCost;
        }
        var now = timeProvider.GetUtcNow();
        if (state.Exists)
        {
            const string update = """
                UPDATE dbo.InventoryBalances SET QuantityOnHand=@Quantity,AverageUnitCost=@Average,InventoryValue=@Value,
                  LastProcessingSequence=@Sequence,UpdatedAt=@Now WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
                """;
            await using var command = new SqlCommand(update, session.Connection, session.Transaction);
            AddBalanceParameters(command, operation, line.ProductId, warehouseId, afterQuantity, afterAverage, afterValue, session.ProcessingSequence, now);
            if(await command.ExecuteNonQueryAsync(cancellationToken)!=1) throw new DBConcurrencyException("The inventory balance could not be updated.");
        }
        else
        {
            const string insert = """
                INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
                VALUES(@BusinessId,@WarehouseId,@ProductId,@Quantity,@Average,@Value,@Sequence,@Now);
                """;
            await using var command = new SqlCommand(insert, session.Connection, session.Transaction);
            AddBalanceParameters(command, operation, line.ProductId, warehouseId, afterQuantity, afterAverage, afterValue, session.ProcessingSequence, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string movement = """
            INSERT dbo.InventoryMovements(InventoryMovementId,BusinessId,WarehouseId,DocumentId,DocumentType,LineNumber,
              ProductId,MovementType,QuantityChange,ProcessingSequence,QuantityBefore,QuantityAfter,AverageUnitCostBefore,
              AverageUnitCostAfter,RecognizedUnitCost,ValueChange,OccurredAt,PostedAt,CreatedAt)
            VALUES(@Id,@BusinessId,@WarehouseId,@DocumentId,@DocumentType,@Line,@ProductId,@MovementType,@Change,@Sequence,
              @BeforeQuantity,@AfterQuantity,@BeforeAverage,@AfterAverage,@RecognizedCost,@ValueChange,@OccurredAt,@Now,@Now);
            """;
        await using (var command = new SqlCommand(movement, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@Id", ids.NewId()); command.Parameters.AddWithValue("@BusinessId", operation.BusinessId); command.Parameters.AddWithValue("@WarehouseId", warehouseId);
            command.Parameters.AddWithValue("@DocumentId", operation.DocumentId); command.Parameters.AddWithValue("@DocumentType", operation.DocumentType); command.Parameters.AddWithValue("@Line", line.LineNumber);
            command.Parameters.AddWithValue("@ProductId", line.ProductId); command.Parameters.AddWithValue("@MovementType", movementType); AddDecimal(command,"@Change",quantityChange,19,6);
            command.Parameters.AddWithValue("@Sequence",session.ProcessingSequence); AddDecimal(command,"@BeforeQuantity",beforeQuantity,19,6); AddDecimal(command,"@AfterQuantity",afterQuantity,19,6);
            AddDecimal(command,"@BeforeAverage",beforeAverage,19,6); AddDecimal(command,"@AfterAverage",afterAverage,19,6); AddDecimal(command,"@RecognizedCost",recognizedCost,19,6);
            AddDecimal(command,"@ValueChange",valueChange,19,4); command.Parameters.AddWithValue("@OccurredAt",operation.OccurredAt); command.Parameters.AddWithValue("@Now",now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        state.Quantity=afterQuantity; state.AverageCost=afterAverage; state.Value=afterValue; state.Exists=true;
        if(updateLine) await UpdateLineResultAsync(session,operation.DocumentId,line.LineNumber,recognizedCost,valueChange,cancellationToken);
        return valueChange;
    }

    private static async Task UpdateLineResultAsync(SqlDocumentProcessingSessionAccessor.Session session,Guid documentId,int line,decimal cost,decimal value,CancellationToken cancellationToken)
    {
        const string sql="UPDATE dbo.InventoryOperationLines SET ProcessedUnitCost=@Cost,ProcessedValue=@Value WHERE InventoryOperationId=@Id AND LineNumber=@Line;";
        await using var command=new SqlCommand(sql,session.Connection,session.Transaction);command.Parameters.AddWithValue("@Id",documentId);command.Parameters.AddWithValue("@Line",line);AddDecimal(command,"@Cost",cost,19,6);AddDecimal(command,"@Value",value,19,4);await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertOutboxAsync(SqlDocumentProcessingSessionAccessor.Session session,InventoryOperationDocumentPayload operation,string payload,CancellationToken cancellationToken)
    {
        const string sql="INSERT dbo.ServerOutboxMessages(MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt) VALUES(@Id,@DocumentId,@DocumentType,N'inventory.operation.processed',@Payload,@Now);";
        await using var command=new SqlCommand(sql,session.Connection,session.Transaction);command.Parameters.AddWithValue("@Id",ids.NewId());command.Parameters.AddWithValue("@DocumentId",operation.DocumentId);command.Parameters.AddWithValue("@DocumentType",operation.DocumentType);command.Parameters.AddWithValue("@Payload",payload);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkProcessedAsync(SqlDocumentProcessingSessionAccessor.Session session,InventoryOperationDocumentPayload operation,decimal total,CancellationToken cancellationToken)
    {
        const string sql="UPDATE dbo.InventoryOperations SET Status=N'Processed',ProcessedAt=@Now,TotalValueChange=@Total WHERE InventoryOperationId=@Id AND BusinessId=@BusinessId AND Status=N'Accepted';";
        await using var command=new SqlCommand(sql,session.Connection,session.Transaction);command.Parameters.AddWithValue("@Id",operation.DocumentId);command.Parameters.AddWithValue("@BusinessId",operation.BusinessId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());AddDecimal(command,"@Total",total,19,4);if(await command.ExecuteNonQueryAsync(cancellationToken)!=1)throw new DBConcurrencyException("The inventory operation could not be marked as processed.");
    }

    private static void AddBalanceParameters(SqlCommand command,InventoryOperationDocumentPayload operation,Guid productId,Guid warehouseId,decimal quantity,decimal average,decimal value,long sequence,DateTimeOffset now)
    {command.Parameters.AddWithValue("@BusinessId",operation.BusinessId);command.Parameters.AddWithValue("@WarehouseId",warehouseId);command.Parameters.AddWithValue("@ProductId",productId);AddDecimal(command,"@Quantity",quantity,19,6);AddDecimal(command,"@Average",average,19,6);AddDecimal(command,"@Value",value,19,4);command.Parameters.AddWithValue("@Sequence",sequence);command.Parameters.AddWithValue("@Now",now);}
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=precision;p.Scale=scale;p.Value=value;}
    private sealed class BalanceState(decimal quantity,decimal averageCost,decimal value,bool exists){public decimal Quantity{get;set;}=quantity;public decimal AverageCost{get;set;}=averageCost;public decimal Value{get;set;}=value;public bool Exists{get;set;}=exists;}
}

public sealed class SqlStockCountDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.StockCount; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlInventoryAdjustmentDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Adjustment; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlWarehouseTransferDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Transfer; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlProductConversionDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Conversion; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
