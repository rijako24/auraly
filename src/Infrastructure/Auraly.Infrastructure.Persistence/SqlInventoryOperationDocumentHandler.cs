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
    SqlInventoryLedgerWriter inventoryWriter,
    TimeProvider timeProvider)
{
    public async Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken)
    {
        var operation = InventoryOperationContractSerializer.Deserialize(document.Payload);
        if (operation.DocumentId != document.DocumentId.Value || operation.BusinessId != document.BusinessId.Value || operation.TenantId != document.TenantId.Value || operation.DocumentType != document.DocumentType)
            throw new InvalidOperationException("The inventory operation envelope does not match its payload.");
        if (operation.DocumentType == InventoryDocumentTypes.Conversion)
            ValidateConversionSnapshot(operation);
        var session = sessions.Current;
        var normalizedLines = new List<InventoryOperationLineSnapshot>(operation.Lines.Count);
        foreach (var line in operation.Lines)
        {
            var target = await SqlProductLinkResolution.ResolveInventoryAsync(
                session, operation.BusinessId, line.ProductId, cancellationToken);
            if (operation.DocumentType == InventoryDocumentTypes.Conversion &&
                (target.ProductId != line.ProductId || target.Factor != 1m))
                throw new InvalidOperationException("A conversion product must keep its own inventory.");
            normalizedLines.Add(line with
            {
                ProductId = target.ProductId,
                Quantity = line.Quantity * target.Factor,
                SystemQuantityAtBase = line.SystemQuantityAtBase * target.Factor,
                ExplicitUnitCost = line.ExplicitUnitCost / target.Factor,
                DispatchUnitCost = line.DispatchUnitCost / target.Factor
            });
        }
        operation = operation with { Lines = normalizedLines };
        if (operation.DocumentType == InventoryDocumentTypes.TransferDispatch)
        {
            await ProcessTransferDispatchAsync(session, operation, document.Payload, cancellationToken);
            return;
        }
        if (operation.DocumentType == InventoryDocumentTypes.TransferReceipt)
        {
            await ProcessTransferReceiptAsync(session, operation, document, cancellationToken);
            return;
        }
        if (operation.DocumentType == InventoryDocumentTypes.StockCount &&
            operation.Lines.GroupBy(line => line.ProductId).Any(group => group.Count() > 1))
            throw new InvalidOperationException("A stock count cannot contain two presentations of the same inventory product.");
        var balances = await LockBalancesAsync(session, operation, cancellationToken);
        var totalValueChange = operation.DocumentType switch
        {
            InventoryDocumentTypes.StockCount => await ProcessCountAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Adjustment => await ProcessAdjustmentAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Transfer => await ProcessTransferAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Conversion => await ProcessConversionAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Damage => await ProcessDamageAsync(session, operation, balances, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported inventory operation '{operation.DocumentType}'.")
        };
        await InsertOutboxAsync(session, operation, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, operation, totalValueChange, cancellationToken);
        if ((operation.DocumentType is InventoryDocumentTypes.StockCount or
                InventoryDocumentTypes.Adjustment or InventoryDocumentTypes.Damage or
                InventoryDocumentTypes.Conversion) &&
            await HasRecognizedAccountingValueAsync(session, operation.DocumentId, cancellationToken))
            await SqlAccountingPostingJobWriter.InsertAsync(
                session, document, operation.OccurredAt, ids, timeProvider, cancellationToken);
        if (operation.DocumentType == InventoryDocumentTypes.StockCount)
            await CompleteCoordinatedPhysicalCountAsync(session, operation, cancellationToken);
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
            WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
              AND p.ProductId=@ProductId AND p.IsActive=1;
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

    private async Task<decimal> ProcessDamageAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var damagedWarehouse = operation.DestinationWarehouseId
            ?? throw new InvalidOperationException("The damaged inventory warehouse is missing.");
        var total = 0m;
        foreach (var line in operation.Lines.OrderBy(line => line.LineNumber))
        {
            total += await ApplyAsync(session, operation, line, operation.WarehouseId, -line.Quantity,
                null, "InventoryDamage", balances, cancellationToken);
            total += await ApplyAsync(session, operation, line, damagedWarehouse, line.Quantity,
                0m, "DamageWarehouseIn", balances, cancellationToken, false);
        }
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

    private async Task ProcessTransferDispatchAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload stage,
        string payload,
        CancellationToken cancellationToken)
    {
        var transit = stage.DestinationWarehouseId ?? throw new InvalidOperationException("The transit warehouse is missing.");
        var posting = stage;
        var balances = await LockBalancesAsync(session, posting, cancellationToken);
        foreach (var line in posting.Lines.OrderBy(line => line.LineNumber))
        {
            var source = balances[(posting.WarehouseId, line.ProductId)];
            if (source.Quantity < line.Quantity)
                throw new InvalidOperationException($"Insufficient inventory for product '{line.ProductCode}'.");
            var cost = source.AverageCost;
            await ApplyAsync(session, posting, line, posting.WarehouseId, -line.Quantity, null,
                "TransferDispatchOut", balances, cancellationToken, false);
            await ApplyAsync(session, posting, line, transit, line.Quantity, cost,
                "TransferDispatchInTransit", balances, cancellationToken, false);
            const string updateLine = """
                UPDATE dbo.InventoryOperationLines
                SET DispatchUnitCost=@Cost,DispatchValue=@Value,ProcessedUnitCost=@Cost,ProcessedValue=@Value
                WHERE InventoryOperationId=@Id AND LineNumber=@Line;
                """;
            await using var command = new SqlCommand(updateLine, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@Id", posting.DocumentId);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            AddDecimal(command, "@Cost", cost, 19, 6);
            AddDecimal(command, "@Value", InventoryOperationRules.Money(line.Quantity * cost), 19, 4);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string update = """
            UPDATE dbo.InventoryOperations
            SET Status=N'Dispatched',DispatchedAt=@Now,DispatchedByUserId=@UserId,TotalValueChange=0
            WHERE InventoryOperationId=@Id AND BusinessId=@BusinessId AND Status=N'DispatchPending';
            """;
        await using (var command = new SqlCommand(update, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@UserId", stage.ConfirmedByUserId);
            command.Parameters.AddWithValue("@Id", stage.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", stage.BusinessId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new DBConcurrencyException("The transfer could not be marked as dispatched.");
        }
        await InsertOutboxAsync(session, stage, payload, cancellationToken);
    }

    private async Task ProcessTransferReceiptAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload stage,
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var transferId = stage.Lines.Select(line => line.TransferId).Distinct().SingleOrDefault()
            ?? throw new InvalidOperationException("The receipt does not identify its transfer.");
        var destination = stage.DestinationWarehouseId ?? throw new InvalidOperationException("The destination warehouse is missing.");
        var posting = stage;
        var balances = await LockBalancesAsync(session, posting, cancellationToken);
        foreach (var line in posting.Lines.OrderBy(line => line.LineNumber))
        {
            var cost = line.DispatchUnitCost ?? throw new InvalidOperationException("The dispatch cost snapshot is missing.");
            var lost = line.TransferLossQuantity ?? 0m;
            if (line.Quantity > 0)
            {
                await ApplyTransferReceiptOutboundAsync(session, posting, line, posting.WarehouseId,
                    line.Quantity, cost, "TransferReceiptOutTransit", balances, cancellationToken);
                await ApplyAsync(session, posting, line, destination, line.Quantity, cost,
                    "TransferReceiptIn", balances, cancellationToken, false);
            }
            if (lost > 0)
                await ApplyTransferReceiptOutboundAsync(session, posting, line, posting.WarehouseId,
                    lost, cost, "TransferLoss", balances, cancellationToken);
            const string updateReceiptLine = """
                UPDATE dbo.InventoryTransferReceiptLines
                SET ProcessedUnitCost=@Cost,ProcessedValue=@Value,ProcessedLossValue=@LossValue
                WHERE InventoryTransferReceiptId=@ReceiptId AND LineNumber=@Line;
                UPDATE dbo.InventoryOperationLines
                SET ReceivedQuantity=ReceivedQuantity+@Quantity,LostQuantity=COALESCE(LostQuantity,0)+@LostQuantity
                WHERE InventoryOperationId=@TransferId AND LineNumber=@Line
                  AND ReceivedQuantity+COALESCE(LostQuantity,0)+@Quantity+@LostQuantity<=DispatchedQuantity;
                IF @@ROWCOUNT<>1 THROW 51230,'The received quantity exceeds the dispatched quantity.',1;
                """;
            await using var command = new SqlCommand(updateReceiptLine, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@ReceiptId", stage.DocumentId);
            command.Parameters.AddWithValue("@TransferId", transferId);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
            AddDecimal(command, "@LostQuantity", lost, 19, 6);
            AddDecimal(command, "@Cost", cost, 19, 6);
            AddDecimal(command, "@Value", InventoryOperationRules.Money(line.Quantity * cost), 19, 4);
            AddDecimal(command, "@LossValue", InventoryOperationRules.Money(lost * cost), 19, 4);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var now = timeProvider.GetUtcNow();
        const string finish = """
            UPDATE dbo.InventoryTransferReceipts SET Status=N'Processed',ProcessedAt=@Now
            WHERE InventoryTransferReceiptId=@ReceiptId AND BusinessId=@BusinessId AND Status=N'Accepted';
            IF @@ROWCOUNT<>1 THROW 51231,'The transfer receipt could not be completed.',1;
            UPDATE o SET
              Status=CASE WHEN EXISTS(
                SELECT 1 FROM dbo.InventoryOperationLines l
                WHERE l.InventoryOperationId=o.InventoryOperationId AND l.ReceivedQuantity+COALESCE(l.LostQuantity,0)<l.DispatchedQuantity)
                THEN N'PartiallyReceived' ELSE N'Received' END,
              ReceivedAt=CASE WHEN EXISTS(
                SELECT 1 FROM dbo.InventoryOperationLines l
                WHERE l.InventoryOperationId=o.InventoryOperationId AND l.ReceivedQuantity+COALESCE(l.LostQuantity,0)<l.DispatchedQuantity)
                THEN NULL ELSE @Now END,
              ReceivedByUserId=CASE WHEN EXISTS(
                SELECT 1 FROM dbo.InventoryOperationLines l
                WHERE l.InventoryOperationId=o.InventoryOperationId AND l.ReceivedQuantity+COALESCE(l.LostQuantity,0)<l.DispatchedQuantity)
                THEN NULL ELSE @UserId END
            FROM dbo.InventoryOperations o
            WHERE o.InventoryOperationId=@TransferId AND o.BusinessId=@BusinessId AND o.Status=N'ReceiptPending';
            IF @@ROWCOUNT<>1 THROW 51232,'The transfer state changed while its receipt was processed.',1;
            """;
        await using (var command = new SqlCommand(finish, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@UserId", stage.ConfirmedByUserId);
            command.Parameters.AddWithValue("@ReceiptId", stage.DocumentId);
            command.Parameters.AddWithValue("@TransferId", transferId);
            command.Parameters.AddWithValue("@BusinessId", stage.BusinessId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertOutboxAsync(session, stage, document.Payload, cancellationToken);
        if (stage.Lines.Any(line => line.TransferLossQuantity > 0))
            await SqlAccountingPostingJobWriter.InsertAsync(
                session, document, stage.OccurredAt, ids, timeProvider, cancellationToken);
    }

    private async Task ApplyTransferReceiptOutboundAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation,
        InventoryOperationLineSnapshot line,
        Guid warehouseId,
        decimal quantity,
        decimal frozenUnitCost,
        string movementType,
        Dictionary<(Guid, Guid), BalanceState> balances,
        CancellationToken cancellationToken)
    {
        var state = balances[(warehouseId, line.ProductId)];
        if (state.Quantity < quantity)
            throw new InvalidOperationException("The transit inventory is insufficient for this receipt.");
        var beforeQuantity = state.Quantity;
        var beforeAverage = state.AverageCost;
        var afterQuantity = InventoryOperationRules.Quantity(beforeQuantity - quantity);
        var valueChange = -InventoryOperationRules.Money(quantity * frozenUnitCost);
        var afterValue = afterQuantity == 0 ? 0m : InventoryOperationRules.Money(state.Value + valueChange);
        if (afterValue < 0)
            throw new InvalidOperationException("The frozen transfer cost exceeds the inventory value remaining in transit.");
        var afterAverage = afterQuantity == 0 ? 0m : InventoryOperationRules.Quantity(afterValue / afterQuantity);
        await inventoryWriter.WriteCalculatedAsync(session, new CalculatedInventoryLedgerPosting(
            operation.BusinessId, warehouseId, line.ProductId, operation.DocumentId, operation.DocumentType,
            line.LineNumber, movementType, state.Exists, -quantity, beforeQuantity, afterQuantity,
            beforeAverage, afterAverage, frozenUnitCost, valueChange, afterValue, operation.OccurredAt), cancellationToken);
        state.Quantity = afterQuantity;
        state.AverageCost = afterAverage;
        state.Value = afterValue;
        state.Exists = true;
    }

    private async Task<decimal> ProcessConversionAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var inputs = operation.Lines.Where(line => line.Direction == "INPUT").OrderBy(line => line.LineNumber).ToArray();
        var outputs = operation.Lines.Where(line => line.Direction == "OUTPUT").OrderBy(line => line.LineNumber).ToArray();
        foreach (var input in inputs.GroupBy(line => line.ProductId))
            if (balances[(operation.WarehouseId, input.Key)].Quantity < input.Sum(line => line.Quantity))
                throw new InvalidOperationException("The conversion input inventory is insufficient.");
        var inputCost = 0m;
        foreach (var line in inputs)
            inputCost -= await ApplyAsync(session, operation, line, operation.WarehouseId, -line.Quantity, null, "ConversionInput", balances, cancellationToken);
        var lossCost = operation.ConversionLossQuantity > 0
            ? InventoryOperationRules.Money(inputCost * operation.ConversionLossQuantity.Value /
                operation.ConversionInputEquivalent!.Value)
            : 0m;
        var allocations = InventoryOperationRules.AllocateConversionCost(
            inputCost - lossCost,
            outputs.Select(line => (line.Quantity, line.AllocationWeight)).ToArray());
        var total = -inputCost;
        for (var index = 0; index < outputs.Length; index++)
        {
            var unitCost = InventoryOperationRules.Quantity(allocations[index] / outputs[index].Quantity);
            total += await ApplyAsync(session, operation, outputs[index], operation.WarehouseId, outputs[index].Quantity, unitCost, "ConversionOutput", balances, cancellationToken);
        }
        return InventoryOperationRules.Money(total);
    }

    private static void ValidateConversionSnapshot(InventoryOperationDocumentPayload operation)
    {
        if (operation.ConversionFamilyRootProductId is null ||
            operation.ConversionMaximumLossPercent is null ||
            operation.ConversionInputEquivalent is null ||
            operation.ConversionOutputEquivalent is null ||
            operation.ConversionLossQuantity is null ||
            operation.ConversionLossPercent is null ||
            operation.Lines.Any(line => line.ConversionFactor is null || line.ConversionEquivalentQuantity is null))
            throw new InvalidOperationException("The conversion configuration snapshot is incomplete.");

        ProductConversionEquivalence calculated;
        try
        {
            calculated = InventoryOperationRules.ValidateConversionEquivalence(
                operation.ConversionType ?? string.Empty,
                operation.Lines.Select(line => (line.Direction, line.Quantity, line.ConversionFactor!.Value)).ToArray(),
                operation.ConversionMaximumLossPercent.Value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
        if (calculated.InputEquivalent != operation.ConversionInputEquivalent ||
            calculated.OutputEquivalent != operation.ConversionOutputEquivalent ||
            calculated.LossQuantity != operation.ConversionLossQuantity ||
            calculated.LossPercent != operation.ConversionLossPercent ||
            operation.Lines.Select(line => line.ConversionEquivalentQuantity!.Value)
                .Where((value, index) => value != calculated.EquivalentQuantities[index]).Any())
            throw new InvalidOperationException("The conversion configuration snapshot is inconsistent.");
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
            recognizedCost = state.AverageCost;
            afterQuantity = InventoryOperationRules.Quantity(state.Quantity - outgoing);
            valueChange = -InventoryOperationRules.Money(outgoing * recognizedCost);
            afterValue = afterQuantity == 0 ? 0m : InventoryOperationRules.Money(state.Value + valueChange);
            afterAverage = afterQuantity == 0 ? 0m : state.AverageCost;
        }
        await inventoryWriter.WriteCalculatedAsync(
            session,
            new CalculatedInventoryLedgerPosting(
                operation.BusinessId,
                warehouseId,
                line.ProductId,
                operation.DocumentId,
                operation.DocumentType,
                line.LineNumber,
                movementType,
                state.Exists,
                quantityChange,
                beforeQuantity,
                afterQuantity,
                beforeAverage,
                afterAverage,
                recognizedCost,
                valueChange,
                afterValue,
                operation.OccurredAt),
            cancellationToken);
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

    private static async Task<bool> HasRecognizedAccountingValueAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS
            (
              SELECT 1 FROM dbo.InventoryOperationLines
              WHERE InventoryOperationId=@DocumentId AND ProcessedValue<>0
            ) THEN 1 ELSE 0 END AS bit);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task CompleteCoordinatedPhysicalCountAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.InventoryPhysicalCounts
            SET Status=N'Closed',ClosedAt=@Now,FinalDocumentNumber=@DocumentNumber
            WHERE BusinessId=@BusinessId AND FinalInventoryOperationId=@DocumentId AND Status=N'Closing';

            UPDATE reconciliation SET
              CountedApplicationStatus=CASE WHEN CountedDocumentId=@DocumentId THEN N'Applied' ELSE CountedApplicationStatus END,
              CountedDocumentNumber=CASE WHEN CountedDocumentId=@DocumentId THEN @DocumentNumber ELSE CountedDocumentNumber END,
              UncountedApplicationStatus=CASE WHEN UncountedDocumentId=@DocumentId THEN N'Applied' ELSE UncountedApplicationStatus END,
              UncountedDocumentNumber=CASE WHEN UncountedDocumentId=@DocumentId THEN @DocumentNumber ELSE UncountedDocumentNumber END
            FROM dbo.InventoryPhysicalCountReconciliations reconciliation
            INNER JOIN dbo.InventoryPhysicalCounts countHeader ON countHeader.InventoryPhysicalCountId=reconciliation.InventoryPhysicalCountId
            WHERE countHeader.BusinessId=@BusinessId AND (reconciliation.CountedDocumentId=@DocumentId OR reconciliation.UncountedDocumentId=@DocumentId);

            UPDATE reconciliation SET Status=N'Applied',AppliedAt=@Now
            FROM dbo.InventoryPhysicalCountReconciliations reconciliation
            INNER JOIN dbo.InventoryPhysicalCounts countHeader ON countHeader.InventoryPhysicalCountId=reconciliation.InventoryPhysicalCountId
            WHERE countHeader.BusinessId=@BusinessId AND reconciliation.Status=N'Active'
              AND (reconciliation.CountedProductCount=0 OR reconciliation.CountedApplicationStatus=N'Applied')
              AND (reconciliation.UncountedProductCount=0 OR reconciliation.UncountedApplicationStatus=N'Applied');

            UPDATE countHeader SET Status=N'Closed',ClosedAt=@Now,FinalInventoryOperationId=@DocumentId,FinalDocumentNumber=@DocumentNumber
            FROM dbo.InventoryPhysicalCounts countHeader
            INNER JOIN dbo.InventoryPhysicalCountReconciliations reconciliation ON reconciliation.InventoryPhysicalCountId=countHeader.InventoryPhysicalCountId
            WHERE countHeader.BusinessId=@BusinessId AND countHeader.Status=N'Reconciling' AND reconciliation.Status=N'Applied';
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@DocumentNumber", operation.DocumentNumber);
        command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", operation.DocumentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=precision;p.Scale=scale;p.Value=value;}
    private sealed class BalanceState(decimal quantity,decimal averageCost,decimal value,bool exists){public decimal Quantity{get;set;}=quantity;public decimal AverageCost{get;set;}=averageCost;public decimal Value{get;set;}=value;public bool Exists{get;set;}=exists;}
}

public sealed class SqlStockCountDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.StockCount; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlInventoryAdjustmentDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Adjustment; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlWarehouseTransferDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Transfer; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlWarehouseTransferDispatchDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.TransferDispatch; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlWarehouseTransferReceiptDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.TransferReceipt; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
public sealed class SqlProductConversionDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Conversion; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }

public sealed class SqlInventoryDamageDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Damage; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
