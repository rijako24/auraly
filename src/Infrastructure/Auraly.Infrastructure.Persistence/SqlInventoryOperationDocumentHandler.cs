using Auraly.Commerce.Accounting.Infrastructure;
using System.Data;
using System.Text.Json;
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
        if (operation.DocumentType == InventoryDocumentTypes.Production &&
            (operation.RecipeVersionId is null ||
             operation.Lines.Count(line => line.Direction == "OUTPUT") != 1 ||
             operation.Lines.All(line => line.Direction != "INPUT") ||
             operation.Lines.Any(line => line.Direction is not ("INPUT" or "OUTPUT") || line.Quantity <= 0)))
            throw new InvalidOperationException("The production recipe snapshot is invalid.");
        var session = sessions.Current;
        var inventoryProducts = await SqlProductLinkResolution.ResolveInventoryBatchAsync(
            session, operation.BusinessId, operation.Lines.Select(line => line.ProductId).ToArray(), cancellationToken);
        var normalizedLines = new List<InventoryOperationLineSnapshot>(operation.Lines.Count);
        foreach (var line in operation.Lines)
        {
            var target = inventoryProducts[line.ProductId];
            if ((operation.DocumentType is InventoryDocumentTypes.Conversion or InventoryDocumentTypes.Production) &&
                (target.ProductId != line.ProductId || target.Factor != 1m))
                throw new InvalidOperationException("A conversion or production product must keep its own inventory.");
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
            InventoryDocumentTypes.Production => await ProcessProductionAsync(session, operation, balances, cancellationToken),
            InventoryDocumentTypes.Damage => await ProcessDamageAsync(session, operation, balances, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported inventory operation '{operation.DocumentType}'.")
        };
        await InsertOutboxAsync(session, operation, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, operation, totalValueChange, cancellationToken);
        if ((operation.DocumentType is InventoryDocumentTypes.StockCount or
                InventoryDocumentTypes.Adjustment or InventoryDocumentTypes.Damage or
                InventoryDocumentTypes.Conversion or InventoryDocumentTypes.Production) &&
            await HasRecognizedAccountingEffectAsync(
                session, operation.DocumentId, operation.DocumentType, cancellationToken))
            await SqlAccountingPostingJobWriter.InsertAsync(
                session.Connection, session.Transaction, document, operation.OccurredAt, ids, timeProvider, cancellationToken);
        if (operation.DocumentType == InventoryDocumentTypes.StockCount)
            await CompleteCoordinatedPhysicalCountAsync(session, operation, cancellationToken);
    }

    private static async Task<Dictionary<(Guid Warehouse, Guid Product), BalanceState>> LockBalancesAsync(
        SqlDocumentProcessingSessionAccessor.Session session, InventoryOperationDocumentPayload operation, CancellationToken cancellationToken)
    {
        var keys = operation.Lines.Select(line => (operation.WarehouseId, line.ProductId)).ToList();
        if (operation.DestinationWarehouseId is { } destination)
            keys.AddRange(operation.Lines.Select(line => (destination, line.ProductId)));
        var orderedKeys = keys.Distinct().OrderBy(key => key.Item1)
            .ThenBy(key => key.ProductId).ToArray();
        var result = new Dictionary<(Guid, Guid), BalanceState>(orderedKeys.Length);
        const string sql = """
            DECLARE @Requested TABLE(WarehouseId UNIQUEIDENTIFIER NOT NULL,
              ProductId UNIQUEIDENTIFIER NOT NULL,
              PRIMARY KEY CLUSTERED(WarehouseId,ProductId));
            INSERT @Requested(WarehouseId,ProductId)
            SELECT WarehouseId,ProductId
            FROM OPENJSON(@Keys) WITH(WarehouseId UNIQUEIDENTIFIER,
              ProductId UNIQUEIDENTIFIER);

            SELECT requested.WarehouseId,requested.ProductId,
                   w.WarehouseId,p.ManageStock,
                   b.QuantityOnHand,b.AverageUnitCost,b.InventoryValue
            FROM @Requested requested
            LEFT JOIN dbo.Products p WITH(UPDLOCK,HOLDLOCK)
              ON p.ProductId=requested.ProductId
             AND p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
             AND p.IsActive=1
            LEFT JOIN dbo.Warehouses w WITH(UPDLOCK,HOLDLOCK)
              ON w.WarehouseId=requested.WarehouseId AND w.BusinessId=@BusinessId
            LEFT JOIN dbo.InventoryBalances b WITH(UPDLOCK,HOLDLOCK)
              ON b.BusinessId=@BusinessId AND b.WarehouseId=requested.WarehouseId
             AND b.ProductId=requested.ProductId
            ORDER BY requested.WarehouseId,requested.ProductId
            OPTION(FORCE ORDER,LOOP JOIN,MAXDOP 1);
            """;
        await using var command = new SqlCommand(sql, session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
        command.Parameters.Add("@Keys", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(orderedKeys.Select(key => new {
                WarehouseId = key.Item1, key.ProductId }));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(2) || reader.IsDBNull(3) || !reader.GetBoolean(3))
                throw new InvalidOperationException("An inventory product or warehouse is no longer valid.");
            result[(reader.GetGuid(0), reader.GetGuid(1))] = new BalanceState(
                reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                !reader.IsDBNull(4));
        }
        if (result.Count != orderedKeys.Length)
            throw new InvalidOperationException("An inventory product or warehouse is no longer valid.");
        return result;
    }

    private async Task<decimal> ProcessCountAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var postings = new List<InventoryLedgerPosting>(operation.Lines.Count);
        var lineResults = new List<LineResult>(operation.Lines.Count);
        foreach (var line in operation.Lines.OrderBy(line => line.LineNumber))
        {
            var change = InventoryOperationRules.CountAdjustment(line.Quantity, line.SystemQuantityAtBase ?? throw new InvalidOperationException("The stock count base is missing."));
            if (change == 0)
                lineResults.Add(new(line.LineNumber, balances[(operation.WarehouseId, line.ProductId)].AverageCost, 0m));
            else
                postings.Add(new(operation.BusinessId, operation.WarehouseId, line.ProductId, operation.DocumentId,
                    operation.DocumentType, line.LineNumber, "StockCountAdjustment", change, null,
                    InventoryValuationMode.AverageCost, operation.OccurredAt));
        }
        var results = await ApplyBatchAsync(session, postings, balances, cancellationToken);
        lineResults.AddRange(postings.Select((posting, index) =>
            new LineResult(posting.LineNumber, results[index].RecognizedUnitCost, results[index].ValueChange)));
        await UpdateLineResultsAsync(session, operation.DocumentId, lineResults, cancellationToken);
        return InventoryOperationRules.Money(results.Sum(result => result.ValueChange));
    }

    private async Task<decimal> ProcessAdjustmentAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var postings = operation.Lines.OrderBy(line => line.LineNumber).Select(line => new InventoryLedgerPosting(
            operation.BusinessId, operation.WarehouseId, line.ProductId, operation.DocumentId, operation.DocumentType,
            line.LineNumber, "InventoryAdjustment", line.Quantity, line.ExplicitUnitCost,
            line.Quantity > 0 && line.ExplicitUnitCost is not null
                ? InventoryValuationMode.WeightedAverageReceipt : InventoryValuationMode.AverageCost,
            operation.OccurredAt)).ToArray();
        var results = await ApplyBatchAsync(session, postings, balances, cancellationToken);
        await UpdateLineResultsAsync(session, operation.DocumentId, postings.Select((posting, index) =>
            new LineResult(posting.LineNumber, results[index].RecognizedUnitCost, results[index].ValueChange)).ToArray(), cancellationToken);
        return InventoryOperationRules.Money(results.Sum(result => result.ValueChange));
    }

    private async Task<decimal> ProcessDamageAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var irreversible = operation.DamageDisposition == "Irreversible";
        var damagedWarehouse = operation.DestinationWarehouseId;
        if (!irreversible && damagedWarehouse is null)
            throw new InvalidOperationException("The damaged inventory warehouse is missing.");
        var postings = new List<InventoryLedgerPosting>(operation.Lines.Count * (irreversible ? 1 : 2));
        var linePositions = new List<(int LineNumber, int Position)>(operation.Lines.Count);
        foreach (var line in operation.Lines.OrderBy(line => line.LineNumber))
        {
            linePositions.Add((line.LineNumber, postings.Count));
            postings.Add(new(operation.BusinessId, operation.WarehouseId, line.ProductId, operation.DocumentId,
                operation.DocumentType, line.LineNumber, "InventoryDamage", -line.Quantity, null,
                InventoryValuationMode.AverageCost, operation.OccurredAt));
            if (!irreversible)
                postings.Add(new(operation.BusinessId, damagedWarehouse!.Value, line.ProductId, operation.DocumentId,
                    operation.DocumentType, line.LineNumber, "DamageWarehouseIn", line.Quantity, 0m,
                    InventoryValuationMode.WeightedAverageReceipt, operation.OccurredAt, OriginSourcePosition: postings.Count-1));
        }
        var results = await ApplyBatchAsync(session, postings, balances, cancellationToken);
        await UpdateLineResultsAsync(session, operation.DocumentId, linePositions.Select(line =>
            new LineResult(line.LineNumber, results[line.Position].RecognizedUnitCost,
                results[line.Position].ValueChange)).ToArray(), cancellationToken);
        return InventoryOperationRules.Money(results.Sum(result => result.ValueChange));
    }

    private async Task<decimal> ProcessTransferAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var destination = operation.DestinationWarehouseId ?? throw new InvalidOperationException("The transfer destination is missing.");
        var postings = new List<InventoryLedgerPosting>(operation.Lines.Count * 2);
        var lines = operation.Lines.OrderBy(line => line.LineNumber).ToArray();
        foreach (var line in lines)
        {
            var sourcePosition = postings.Count;
            postings.Add(new(operation.BusinessId, operation.WarehouseId, line.ProductId, operation.DocumentId,
                operation.DocumentType, line.LineNumber, "TransferOut", -line.Quantity, null,
                InventoryValuationMode.AverageCost, operation.OccurredAt));
            postings.Add(new(operation.BusinessId, destination, line.ProductId, operation.DocumentId,
                operation.DocumentType, line.LineNumber, "TransferIn", line.Quantity, null,
                InventoryValuationMode.WeightedAverageReceipt, operation.OccurredAt, sourcePosition, OriginSourcePosition: sourcePosition));
        }
        var results = await ApplyBatchAsync(session, postings, balances, cancellationToken);
        await UpdateLineResultsAsync(session, operation.DocumentId, lines.Select((line, index) =>
            new LineResult(line.LineNumber, results[index * 2 + 1].RecognizedUnitCost,
                results[index * 2 + 1].ValueChange)).ToArray(), cancellationToken);
        return InventoryOperationRules.Money(results.Sum(result => result.ValueChange));
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
        var available = balances.ToDictionary(item => item.Key, item => item.Value.Quantity);
        var lines = posting.Lines.OrderBy(line => line.LineNumber).ToArray();
        var postings = new List<InventoryLedgerPosting>(lines.Length * 2);
        foreach (var line in lines)
        {
            var key = (posting.WarehouseId, line.ProductId);
            if (available[key] < line.Quantity)
                throw new InvalidOperationException($"Insufficient inventory for product '{line.ProductCode}'.");
            available[key] = InventoryOperationRules.Quantity(available[key] - line.Quantity);
            var sourcePosition = postings.Count;
            postings.Add(new(posting.BusinessId, posting.WarehouseId, line.ProductId, posting.DocumentId,
                posting.DocumentType, line.LineNumber, "TransferDispatchOut", -line.Quantity, null,
                InventoryValuationMode.AverageCost, posting.OccurredAt));
            postings.Add(new(posting.BusinessId, transit, line.ProductId, posting.DocumentId,
                posting.DocumentType, line.LineNumber, "TransferDispatchInTransit", line.Quantity, null,
                InventoryValuationMode.WeightedAverageReceipt, posting.OccurredAt, sourcePosition, OriginSourcePosition: sourcePosition));
        }
        var results = await ApplyBatchAsync(session, postings, balances, cancellationToken);
        await using (var command = new SqlCommand("""
                UPDATE line
                SET DispatchUnitCost=input.Cost,DispatchValue=input.Value,
                    ProcessedUnitCost=input.Cost,ProcessedValue=input.Value
                FROM dbo.InventoryOperationLines line
                JOIN OPENJSON(@Lines) WITH(LineNumber int,Cost decimal(19,6),Value decimal(19,4)) input
                  ON input.LineNumber=line.LineNumber WHERE line.InventoryOperationId=@Id;
                IF @@ROWCOUNT<>@Count THROW 51606,'The transfer dispatch lines changed during processing.',1;
                """, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@Id", posting.DocumentId);
            command.Parameters.AddWithValue("@Count", lines.Length);
            command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
                lines.Select((line, index) => new LineResult(line.LineNumber, results[index * 2].RecognizedUnitCost,
                    InventoryOperationRules.Money(line.Quantity * results[index * 2].RecognizedUnitCost))));
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
        var available = balances.ToDictionary(item => item.Key, item => item.Value.Quantity);
        var lines = posting.Lines.OrderBy(line => line.LineNumber).ToArray();
        var postings = new List<InventoryLedgerPosting>(lines.Length * 3);
        foreach (var line in lines)
        {
            var cost = line.DispatchUnitCost ?? throw new InvalidOperationException("The dispatch cost snapshot is missing.");
            var lost = line.TransferLossQuantity ?? 0m;
            var key = (posting.WarehouseId, line.ProductId);
            if (line.Quantity > 0)
            {
                if (available[key] < line.Quantity)
                    throw new InvalidOperationException("The transit inventory is insufficient for this receipt.");
                available[key] = InventoryOperationRules.Quantity(available[key] - line.Quantity);
                var sourcePosition=postings.Count;
                postings.Add(new(posting.BusinessId, posting.WarehouseId, line.ProductId, posting.DocumentId,
                    posting.DocumentType, line.LineNumber, "TransferReceiptOutTransit", -line.Quantity, cost,
                    InventoryValuationMode.SpecifiedCostIssue, posting.OccurredAt));
                postings.Add(new(posting.BusinessId, destination, line.ProductId, posting.DocumentId,
                    posting.DocumentType, line.LineNumber, "TransferReceiptIn", line.Quantity, cost,
                    InventoryValuationMode.WeightedAverageReceipt, posting.OccurredAt, OriginSourcePosition: sourcePosition));
            }
            if (lost > 0)
            {
                if (available[key] < lost)
                    throw new InvalidOperationException("The transit inventory is insufficient for this receipt.");
                available[key] = InventoryOperationRules.Quantity(available[key] - lost);
                postings.Add(new(posting.BusinessId, posting.WarehouseId, line.ProductId, posting.DocumentId,
                    posting.DocumentType, line.LineNumber, "TransferLoss", -lost, cost,
                    InventoryValuationMode.SpecifiedCostIssue, posting.OccurredAt));
            }
        }
        _ = await ApplyBatchAsync(session, postings, balances, cancellationToken);
        await using (var command = new SqlCommand("""
                DECLARE @Results TABLE(LineNumber int PRIMARY KEY,Quantity decimal(19,6),LostQuantity decimal(19,6),
                  Cost decimal(19,6),Value decimal(19,4),LossValue decimal(19,4));
                INSERT @Results SELECT * FROM OPENJSON(@Lines) WITH(LineNumber int,Quantity decimal(19,6),
                  LostQuantity decimal(19,6),Cost decimal(19,6),Value decimal(19,4),LossValue decimal(19,4));
                UPDATE line SET ProcessedUnitCost=input.Cost,ProcessedValue=input.Value,ProcessedLossValue=input.LossValue
                FROM dbo.InventoryTransferReceiptLines line JOIN @Results input ON input.LineNumber=line.LineNumber
                WHERE line.InventoryTransferReceiptId=@ReceiptId;
                IF @@ROWCOUNT<>@Count THROW 51606,'The transfer receipt lines changed during processing.',1;
                UPDATE line SET ReceivedQuantity=line.ReceivedQuantity+input.Quantity,
                  LostQuantity=COALESCE(line.LostQuantity,0)+input.LostQuantity
                FROM dbo.InventoryOperationLines line JOIN @Results input ON input.LineNumber=line.LineNumber
                WHERE line.InventoryOperationId=@TransferId
                  AND line.ReceivedQuantity+COALESCE(line.LostQuantity,0)+input.Quantity+input.LostQuantity<=line.DispatchedQuantity;
                IF @@ROWCOUNT<>@Count THROW 51230,'The received quantity exceeds the dispatched quantity.',1;
                """, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@ReceiptId", stage.DocumentId);
            command.Parameters.AddWithValue("@TransferId", transferId);
            command.Parameters.AddWithValue("@Count", lines.Length);
            command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines.Select(line => new
            {
                line.LineNumber, line.Quantity, LostQuantity = line.TransferLossQuantity ?? 0m,
                Cost = line.DispatchUnitCost!.Value,
                Value = InventoryOperationRules.Money(line.Quantity * line.DispatchUnitCost.Value),
                LossValue = InventoryOperationRules.Money((line.TransferLossQuantity ?? 0m) * line.DispatchUnitCost.Value)
            }));
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
                session.Connection, session.Transaction, document, stage.OccurredAt, ids, timeProvider, cancellationToken);
    }

    private async Task<decimal> ProcessConversionAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var inputs = operation.Lines.Where(line => line.Direction == "INPUT").OrderBy(line => line.LineNumber).ToArray();
        var outputs = operation.Lines.Where(line => line.Direction == "OUTPUT").OrderBy(line => line.LineNumber).ToArray();
        foreach (var input in inputs.GroupBy(line => line.ProductId))
            if (balances[(operation.WarehouseId, input.Key)].Quantity < input.Sum(line => line.Quantity))
                throw new InvalidOperationException("The conversion input inventory is insufficient.");
        var inputResults = await ApplyBatchAsync(session, inputs.Select(line => new InventoryLedgerPosting(
            operation.BusinessId, operation.WarehouseId, line.ProductId, operation.DocumentId, operation.DocumentType,
            line.LineNumber, "ConversionInput", -line.Quantity, null, InventoryValuationMode.AverageCost,
            operation.OccurredAt)).ToArray(), balances, cancellationToken);
        var inputCost = -inputResults.Sum(result => result.ValueChange);
        var lossCost = operation.ConversionLossQuantity > 0
            ? InventoryOperationRules.Money(inputCost * operation.ConversionLossQuantity.Value /
                operation.ConversionInputEquivalent!.Value)
            : 0m;
        var allocations = InventoryOperationRules.AllocateConversionCost(
            inputCost - lossCost,
            outputs.Select(line => (line.Quantity, line.AllocationWeight)).ToArray());
        var outputResults = await ApplyBatchAsync(session, outputs.Select((line, index) => new InventoryLedgerPosting(
            operation.BusinessId, operation.WarehouseId, line.ProductId, operation.DocumentId, operation.DocumentType,
            line.LineNumber, "ConversionOutput", line.Quantity,
            InventoryOperationRules.Quantity(allocations[index] / line.Quantity),
            InventoryValuationMode.WeightedAverageReceipt, operation.OccurredAt)).ToArray(), balances, cancellationToken);
        await UpdateLineResultsAsync(session, operation.DocumentId, inputs.Select((line, index) =>
            new LineResult(line.LineNumber, inputResults[index].RecognizedUnitCost, inputResults[index].ValueChange))
            .Concat(outputs.Select((line, index) =>
                new LineResult(line.LineNumber, outputResults[index].RecognizedUnitCost, outputResults[index].ValueChange)))
            .ToArray(), cancellationToken);
        return InventoryOperationRules.Money(outputResults.Sum(result => result.ValueChange) - inputCost);
    }

    private async Task<decimal> ProcessProductionAsync(SqlDocumentProcessingSessionAccessor.Session session,
        InventoryOperationDocumentPayload operation, Dictionary<(Guid, Guid), BalanceState> balances,
        CancellationToken cancellationToken)
    {
        var inputs = operation.Lines.Where(line => line.Direction == "INPUT").OrderBy(line => line.LineNumber).ToArray();
        var output = operation.Lines.Single(line => line.Direction == "OUTPUT");
        foreach (var ingredient in inputs.GroupBy(line => line.ProductId))
            if (balances[(operation.WarehouseId, ingredient.Key)].Quantity < ingredient.Sum(line => line.Quantity))
                throw new InvalidOperationException("The production ingredient inventory is insufficient.");
        var inputResults = await ApplyBatchAsync(session, inputs.Select(input => new InventoryLedgerPosting(
            operation.BusinessId, operation.WarehouseId, input.ProductId, operation.DocumentId, operation.DocumentType,
            input.LineNumber, "ProductionInput", -input.Quantity, null, InventoryValuationMode.AverageCost,
            operation.OccurredAt)).ToArray(), balances, cancellationToken);
        var consumedValue = -inputResults.Sum(result => result.ValueChange);
        var unitCost = InventoryOperationRules.Quantity(consumedValue / output.Quantity);
        var outputResults = await ApplyBatchAsync(session, [new(operation.BusinessId, operation.WarehouseId,
            output.ProductId, operation.DocumentId, operation.DocumentType, output.LineNumber, "ProductionOutput",
            output.Quantity, unitCost, InventoryValuationMode.WeightedAverageReceipt, operation.OccurredAt)],
            balances, cancellationToken);
        await UpdateLineResultsAsync(session, operation.DocumentId, inputs.Select((line, index) =>
            new LineResult(line.LineNumber, inputResults[index].RecognizedUnitCost, inputResults[index].ValueChange))
            .Append(new(output.LineNumber, outputResults[0].RecognizedUnitCost, outputResults[0].ValueChange)).ToArray(), cancellationToken);
        return InventoryOperationRules.Money(outputResults[0].ValueChange - consumedValue);
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

    private static void UpdateBalanceStates(
        Dictionary<(Guid Warehouse, Guid Product), BalanceState> balances,
        Guid warehouseId,
        Guid productId,
        InventoryLedgerPostingResult result)
    {
        var target = balances[(warehouseId, productId)];
        target.Quantity = result.QuantityAfter;
        target.Exists = true;
        foreach (var entry in balances.Where(entry => entry.Key.Product == productId))
        {
            entry.Value.AverageCost = result.AverageUnitCostAfter;
            entry.Value.Value = InventoryOperationRules.Money(
                entry.Value.Quantity * result.AverageUnitCostAfter);
        }
    }

    private async Task<IReadOnlyList<InventoryLedgerPostingResult>> ApplyBatchAsync(
        SqlDocumentProcessingSessionAccessor.Session session, IReadOnlyList<InventoryLedgerPosting> postings,
        Dictionary<(Guid, Guid), BalanceState> balances, CancellationToken cancellationToken)
    {
        var results = await inventoryWriter.PostBatchAsync(session, postings, cancellationToken);
        for (var index = 0; index < postings.Count; index++)
            UpdateBalanceStates(balances, postings[index].WarehouseId, postings[index].ProductId, results[index]);
        return results;
    }

    private static async Task UpdateLineResultsAsync(SqlDocumentProcessingSessionAccessor.Session session, Guid documentId,
        IReadOnlyList<LineResult> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0) return;
        await using var command = new SqlCommand("""
            UPDATE line SET ProcessedUnitCost=input.Cost,ProcessedValue=input.Value
            FROM dbo.InventoryOperationLines line
            JOIN OPENJSON(@Lines) WITH(LineNumber int,Cost decimal(19,6),Value decimal(19,4)) input
              ON input.LineNumber=line.LineNumber WHERE line.InventoryOperationId=@Id;
            IF @@ROWCOUNT<>@Count THROW 51606,'The inventory operation lines changed during processing.',1;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@Count", lines.Count);
        command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record LineResult(int LineNumber, decimal Cost, decimal Value);

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

    private static async Task<bool> HasRecognizedAccountingEffectAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS
            (
              SELECT 1 FROM dbo.InventoryOperationLines
              WHERE InventoryOperationId=@DocumentId
              GROUP BY InventoryOperationId
              HAVING
                (@DocumentType IN (N'ProductConversion',N'Production') AND SUM(ProcessedValue)<>0)
                OR
                (@DocumentType NOT IN (N'ProductConversion',N'Production') AND
                 SUM(CASE WHEN ProcessedValue<>0 THEN 1 ELSE 0 END)>0)
            ) THEN 1 ELSE 0 END AS bit);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
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
public sealed class SqlProductionDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Production; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }

public sealed class SqlInventoryDamageDocumentHandler(SqlInventoryOperationProcessor processor) : IConfirmedDocumentHandler { public string DocumentType=>InventoryDocumentTypes.Damage; public Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)=>processor.HandleAsync(document,cancellationToken); }
