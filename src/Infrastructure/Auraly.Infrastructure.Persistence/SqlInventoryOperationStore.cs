using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Inventory;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Inventory;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryOperationStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    SqlDocumentProcessingSessionAccessor processingSessions,
    SqlInventoryOperationProcessor processor) : IInventoryOperationStore
{
    public async Task<StockCountDraft> StartCountAsync(InventoryUserIdentity user, StartStockCountRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var productIds = request.Lines.Select(line => line.ProductId).ToArray();
            var preCounts = request.Lines.ToDictionary(line => line.ProductId, line => line.PreCountQuantity);
            await ValidateScopeAsync(connection, transaction, user, InventoryDocumentTypes.StockCount,
                request.WarehouseId, null, productIds, cancellationToken);
            var reasonDescription = await LoadActiveReasonAsync(connection, transaction, user.BusinessId, InventoryDocumentTypes.StockCount, request.ReasonCode, cancellationToken);
            var baseSequence = await ReadBaseSequenceAsync(connection, transaction, request.BusinessId, cancellationToken);
            var products = await LoadProductsAsync(connection, transaction, request.BusinessId, request.WarehouseId, productIds, cancellationToken);
            var lines = products.OrderBy(product => product.Code, StringComparer.Ordinal).Select((product, index) =>
                new InventoryOperationLineSnapshot(index + 1, "COUNT", product.Id, product.Code, product.Name, 0m, preCounts[product.Id], product.Quantity, null, null)).ToArray();
            var now = timeProvider.GetUtcNow();
            const string insert = """
                INSERT dbo.InventoryOperations
                  (InventoryOperationId,BusinessId,DocumentType,WarehouseId,OccurredAt,ReasonCode,ReasonDescription,
                   BaseInventorySequence,Notes,Status,CreatedAt)
                VALUES(@Id,@BusinessId,N'StockCount',@WarehouseId,@OccurredAt,@Reason,@ReasonDescription,@BaseSequence,@Notes,N'Draft',@Now);
                """;
            await using (var command = new SqlCommand(insert, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", request.DocumentId);
                command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
                command.Parameters.AddWithValue("@Reason", request.ReasonCode);
                command.Parameters.AddWithValue("@ReasonDescription", reasonDescription);
                command.Parameters.AddWithValue("@BaseSequence", baseSequence);
                command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertLinesAsync(connection, transaction, request.DocumentId, lines, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StockCountDraft(request.DocumentId, "Draft", baseSequence, lines);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InventoryConflictException("The stock count DocumentId is already in use.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<InventoryOperationAcceptance> ConfirmCountAsync(InventoryUserIdentity user, Guid documentId, string idempotencyKey, ConfirmStockCountRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var requestHash = Hash(new { documentId, request.BusinessId, request.Lines });
            var replay = await TryReplayAsync(connection, transaction, request.BusinessId, documentId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null) { await transaction.CommitAsync(cancellationToken); return replay; }

            var draft = await LoadCountDraftAsync(connection, transaction, user, documentId, cancellationToken);
            var counted = request.Lines.ToDictionary(line => line.ProductId);
            if (counted.Count != draft.Lines.Count || draft.Lines.Any(line => !counted.ContainsKey(line.ProductId)))
                throw new InventoryValidationException("The confirmed count must contain exactly the products captured at start.");
            var lines = draft.Lines.Select(line => line with { Quantity = counted[line.ProductId].CountedQuantity }).ToArray();
            var acceptance = await AcceptDraftAsync(connection, transaction, user, documentId, InventoryDocumentTypes.StockCount,
                draft.WarehouseId, null, draft.OccurredAt, draft.ReasonCode, null, null, draft.BaseSequence, draft.Notes,
                idempotencyKey, requestHash, lines, null, cancellationToken);
            foreach (var line in lines)
            {
                const string update = "UPDATE dbo.InventoryOperationLines SET Quantity=@Quantity WHERE InventoryOperationId=@Id AND ProductId=@ProductId;";
                await using var command = new SqlCommand(update, connection, transaction);
                command.Parameters.AddWithValue("@Id", documentId);
                command.Parameters.AddWithValue("@ProductId", line.ProductId);
                AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return acceptance;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public Task<InventoryOperationAcceptance> ConfirmAdjustmentAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryAdjustmentRequest request, CancellationToken cancellationToken) =>
        AcceptNewAsync(user, idempotencyKey, request.DocumentId, InventoryDocumentTypes.Adjustment,
            request.WarehouseId, null, request.OccurredAt, request.ReasonCode, null, request.CostCenterId, null, request.Notes,
            request.Lines.Select(line => new LineInput(line.LineNumber, "ADJUSTMENT", line.ProductId, line.QuantityChange, null, line.ExplicitUnitCost, null)).ToArray(), request, cancellationToken);

    public async Task<InventoryOperationAcceptance> DispatchTransferAsync(
        InventoryUserIdentity user,
        string idempotencyKey,
        DispatchWarehouseTransferRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var requestHash = Hash(request);
            var replay = await TryTransferStageReplayAsync(connection, transaction, request.BusinessId, request.DocumentId,
                InventoryDocumentTypes.TransferDispatch, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null) { await transaction.CommitAsync(cancellationToken); return replay; }

            await ValidateScopeAsync(connection, transaction, user, InventoryDocumentTypes.Transfer,
                request.SourceWarehouseId,
                request.DestinationWarehouseId, request.Lines.Select(line => line.ProductId), cancellationToken);
            var transitWarehouseId = await LoadTransitWarehouseAsync(connection, transaction, request.BusinessId, cancellationToken);
            var reasonDescription = await LoadActiveReasonAsync(connection, transaction, user.BusinessId,
                InventoryDocumentTypes.Transfer, request.ReasonCode, cancellationToken);
            var products = (await LoadProductsAsync(connection, transaction, request.BusinessId,
                request.SourceWarehouseId, request.Lines.Select(line => line.ProductId), cancellationToken)).ToDictionary(product => product.Id);
            var lines = request.Lines.Select(line => new InventoryOperationLineSnapshot(
                line.LineNumber, "TRANSFER", line.ProductId, products[line.ProductId].Code,
                products[line.ProductId].Name, line.DispatchedQuantity, null, null, null, null,
                DispatchedQuantity: line.DispatchedQuantity, ReceivedQuantity: 0m, TransferId: request.DocumentId,
                TransferLossQuantity: 0m)).ToArray();
            var now = timeProvider.GetUtcNow();
            const string insert = """
                INSERT dbo.InventoryOperations
                  (InventoryOperationId,BusinessId,DocumentType,WarehouseId,DestinationWarehouseId,TransitWarehouseId,TransferMode,
                   OccurredAt,ReasonCode,ReasonDescription,Notes,Status,CreatedAt)
                VALUES(@Id,@BusinessId,N'WarehouseTransfer',@WarehouseId,@Destination,@Transit,N'DispatchAndReceive',
                   @OccurredAt,@Reason,@ReasonDescription,@Notes,N'Draft',@Now);
                """;
            await using (var command = new SqlCommand(insert, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", request.DocumentId);
                command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                command.Parameters.AddWithValue("@WarehouseId", request.SourceWarehouseId);
                command.Parameters.AddWithValue("@Destination", request.DestinationWarehouseId);
                command.Parameters.AddWithValue("@Transit", transitWarehouseId);
                command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
                command.Parameters.AddWithValue("@Reason", request.ReasonCode);
                command.Parameters.AddWithValue("@ReasonDescription", reasonDescription);
                command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertLinesAsync(connection, transaction, request.DocumentId, lines, cancellationToken);
            var acceptance = await AcceptTransferDispatchAsync(connection, transaction, user, idempotencyKey,
                requestHash, request, transitWarehouseId, lines, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return acceptance;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InventoryConflictException("The transfer document or idempotency key is already in use.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<InventoryOperationAcceptance> ReceiveTransferAsync(
        InventoryUserIdentity user,
        Guid transferId,
        string idempotencyKey,
        ReceiveWarehouseTransferRequest request,
        byte[] rowVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var requestHash = Hash(new { transferId, request });
            var replay = await TryReceiptReplayAsync(connection, transaction, request.BusinessId, request.ReceiptId,
                idempotencyKey, requestHash, cancellationToken);
            if (replay is not null) { await transaction.CommitAsync(cancellationToken); return replay; }

            var transfer = await LoadReceivableTransferAsync(connection, transaction, user, transferId, rowVersion, cancellationToken);
            var requested = request.Lines.ToDictionary(line => line.LineNumber);
            if (requested.Count != transfer.Lines.Count || transfer.Lines.Any(line =>
                    !requested.TryGetValue(line.LineNumber, out var received) || received.ProductId != line.ProductId))
                throw new InventoryValidationException("The receipt must contain exactly the products and lines dispatched by the source warehouse.");
            if (transfer.Lines.Any(line => requested[line.LineNumber].ReceivedQuantity > line.PendingQuantity))
                throw new InventoryValidationException("A receipt cannot exceed the quantity still in transit.");
            var resolvesDifference = !string.IsNullOrWhiteSpace(request.DifferenceReasonCode);
            if (resolvesDifference && !user.Permissions.Contains(InventoryPermissionCodes.ResolveTransferDifference))
                throw new InventoryForbiddenException($"Permission '{InventoryPermissionCodes.ResolveTransferDifference}' is required to confirm a transfer difference.");
            AccountingReasonSnapshot? accountingReason = null;
            if (resolvesDifference)
                accountingReason = await LoadAccountingReasonAsync(
                    connection, transaction, user.BusinessId, InventoryDocumentTypes.Transfer,
                    request.DifferenceReasonCode!, null, request.Notes, true, cancellationToken);

            var now = timeProvider.GetUtcNow();
            const string insert = """
                INSERT dbo.InventoryTransferReceipts
                  (InventoryTransferReceiptId,TransferId,BusinessId,DestinationWarehouseId,IdempotencyKey,RequestHash,
                   DifferenceReasonCode,Notes,Status,ReceivedByUserId,OccurredAt,CreatedAt)
                VALUES(@ReceiptId,@TransferId,@BusinessId,@Destination,@Key,@Hash,@Reason,@Notes,N'Accepted',@UserId,@OccurredAt,@Now);
                """;
            await using (var command = new SqlCommand(insert, connection, transaction))
            {
                command.Parameters.AddWithValue("@ReceiptId", request.ReceiptId);
                command.Parameters.AddWithValue("@TransferId", transferId);
                command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                command.Parameters.AddWithValue("@Destination", transfer.DestinationWarehouseId);
                command.Parameters.AddWithValue("@Key", idempotencyKey);
                command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = requestHash;
                command.Parameters.AddWithValue("@Reason", (object?)request.DifferenceReasonCode ?? DBNull.Value);
                command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            var payloadLines = new List<InventoryOperationLineSnapshot>();
            foreach (var line in transfer.Lines.OrderBy(line => line.LineNumber))
            {
                var received = requested[line.LineNumber].ReceivedQuantity;
                var lost = resolvesDifference ? line.PendingQuantity - received : 0m;
                if (received == 0 && lost == 0) continue;
                const string insertLine = """
                    INSERT dbo.InventoryTransferReceiptLines
                      (InventoryTransferReceiptId,LineNumber,ProductId,ReceivedQuantity,LostQuantity)
                    VALUES(@ReceiptId,@Line,@ProductId,@Quantity,@LostQuantity);
                    """;
                await using var command = new SqlCommand(insertLine, connection, transaction);
                command.Parameters.AddWithValue("@ReceiptId", request.ReceiptId);
                command.Parameters.AddWithValue("@Line", line.LineNumber);
                command.Parameters.AddWithValue("@ProductId", line.ProductId);
                AddDecimal(command, "@Quantity", received, 19, 6);
                AddDecimal(command, "@LostQuantity", lost, 19, 6);
                await command.ExecuteNonQueryAsync(cancellationToken);
                payloadLines.Add(new InventoryOperationLineSnapshot(line.LineNumber, "TRANSFER", line.ProductId,
                    line.ProductCode, line.ProductName, received, null, null, null, null,
                    DispatchedQuantity: line.DispatchedQuantity, ReceivedQuantity: received,
                    DispatchUnitCost: line.DispatchUnitCost, TransferId: transferId,
                    TransferLossQuantity: lost));
            }
            var acceptance = await AcceptTransferReceiptAsync(connection, transaction, user, request, transfer,
                payloadLines, accountingReason, cancellationToken);
            const string pending = "UPDATE dbo.InventoryOperations SET Status=N'ReceiptPending' WHERE InventoryOperationId=@Id AND BusinessId=@BusinessId AND Status IN(N'Dispatched',N'PartiallyReceived');";
            await using (var command = new SqlCommand(pending, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", transferId);
                command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException("The transfer changed while its receipt was being confirmed.");
            }
            await transaction.CommitAsync(cancellationToken);
            return acceptance;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InventoryConflictException("The receipt or idempotency key is already in use.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<InventoryOperationAcceptance> ConfirmSystemTransferAtomicallyAsync(
        InventoryUserIdentity user,
        string idempotencyKey,
        DispatchWarehouseTransferRequest request,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var inputLines = request.Lines
            .Select(line => new LineInput(line.LineNumber, "TRANSFER", line.ProductId, line.DispatchedQuantity, null, null, null))
            .ToArray();
        var requestHash = Hash(request);
        var replay = await TryReplayAsync(connection, transaction, request.BusinessId, request.DocumentId,
            idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            if (!StringComparer.Ordinal.Equals(replay.Status, "Processed"))
                throw new InventoryConflictException("La reserva de inventario anterior todavía no ha terminado.");
            return replay;
        }

        await RequireCurrentProcessingSequenceAsync(connection, transaction, request.BusinessId, cancellationToken);
        await ValidateScopeAsync(connection, transaction, user, InventoryDocumentTypes.Transfer,
            request.SourceWarehouseId,
            request.DestinationWarehouseId, inputLines.Select(line => line.ProductId), cancellationToken,
            allowSystemWarehouses: true);
        var reasonDescription = await LoadActiveReasonAsync(connection, transaction, user.BusinessId,
            InventoryDocumentTypes.Transfer, request.ReasonCode, cancellationToken);
        var products = (await LoadProductsAsync(connection, transaction, request.BusinessId,
            request.SourceWarehouseId, inputLines.Select(line => line.ProductId), cancellationToken)).ToDictionary(product => product.Id);
        var lines = inputLines.Select(line => new InventoryOperationLineSnapshot(
            line.LineNumber, line.Direction, line.ProductId, products[line.ProductId].Code,
            products[line.ProductId].Name, line.Quantity, null, null, null, null,
            DispatchedQuantity: line.Quantity, ReceivedQuantity: line.Quantity, TransferId: request.DocumentId,
            TransferLossQuantity: 0m)).ToArray();
        var now = timeProvider.GetUtcNow();
        const string insert = """
            INSERT dbo.InventoryOperations
              (InventoryOperationId,BusinessId,DocumentType,WarehouseId,DestinationWarehouseId,
               TransferMode,OccurredAt,ReasonCode,ReasonDescription,Notes,Status,CreatedAt)
            VALUES(@Id,@BusinessId,N'WarehouseTransfer',@WarehouseId,@Destination,
               N'ImmediateSystem',@OccurredAt,@Reason,@ReasonDescription,@Notes,N'Draft',@Now);
            """;
        await using (var command = new SqlCommand(insert, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", request.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
            command.Parameters.AddWithValue("@WarehouseId", request.SourceWarehouseId);
            command.Parameters.AddWithValue("@Destination", request.DestinationWarehouseId);
            command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
            command.Parameters.AddWithValue("@Reason", request.ReasonCode);
            command.Parameters.AddWithValue("@ReasonDescription", reasonDescription);
            command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertLinesAsync(connection, transaction, request.DocumentId, lines, cancellationToken);
        var acceptance = await AcceptDraftAsync(connection, transaction, user, request.DocumentId,
            InventoryDocumentTypes.Transfer, request.SourceWarehouseId, request.DestinationWarehouseId,
            request.OccurredAt, request.ReasonCode, null, null, null, request.Notes,
            idempotencyKey, requestHash, lines, null, cancellationToken);
        await ProcessAcceptedInsideTransactionAsync(connection, transaction, user, acceptance, cancellationToken);
        return acceptance with { Status = "Processed" };
    }

    public Task<InventoryOperationAcceptance> ConfirmDamageAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryDamageRequest request, CancellationToken cancellationToken) =>
        AcceptNewAsync(user, idempotencyKey, request.DocumentId, InventoryDocumentTypes.Damage,
            request.WarehouseId, null, request.OccurredAt, request.ReasonCode, null, request.CostCenterId, null, request.Notes,
            request.Lines.Select(line => new LineInput(line.LineNumber, "DAMAGE", line.ProductId, line.Quantity, null, null, null)).ToArray(), request, cancellationToken);

    public Task<InventoryOperationAcceptance> ConfirmConversionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductConversionRequest request, CancellationToken cancellationToken) =>
        AcceptNewAsync(user, idempotencyKey, request.DocumentId, InventoryDocumentTypes.Conversion,
            request.WarehouseId, null, request.OccurredAt, request.ReasonCode, request.ConversionType, request.CostCenterId, null, request.Notes,
            request.Lines.Select(line => new LineInput(line.LineNumber, line.Direction, line.ProductId, line.Quantity, null, null, line.AllocationWeight)).ToArray(), request, cancellationToken);

    private async Task<InventoryOperationAcceptance> AcceptNewAsync(
        InventoryUserIdentity user, string idempotencyKey, Guid documentId, string documentType,
        Guid warehouseId, Guid? destinationWarehouseId, DateTimeOffset occurredAt, string reasonCode,
        string? conversionType, Guid? costCenterId, long? baseSequence, string? notes,
        IReadOnlyList<LineInput> inputLines, object requestForHash, CancellationToken cancellationToken)
    {
        var requestHash = Hash(requestForHash);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await TryReplayAsync(connection, transaction, user.BusinessId, documentId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null) { await transaction.CommitAsync(cancellationToken); return replay; }
            if (documentType == InventoryDocumentTypes.Damage)
                destinationWarehouseId = await LoadSystemWarehouseAsync(
                    connection, transaction, user.BusinessId, "AVE", cancellationToken);
            await ValidateScopeAsync(connection, transaction, user, documentType, warehouseId,
                destinationWarehouseId, inputLines.Select(line => line.ProductId), cancellationToken);
            var reasonDescription = await LoadActiveReasonAsync(connection, transaction, user.BusinessId, documentType, reasonCode, cancellationToken);
            var products = (await LoadProductsAsync(connection, transaction, user.BusinessId, warehouseId, inputLines.Select(line => line.ProductId), cancellationToken)).ToDictionary(product => product.Id);
            var lines = inputLines.Select(line => new InventoryOperationLineSnapshot(
                line.LineNumber, line.Direction, line.ProductId, products[line.ProductId].Code,
                products[line.ProductId].Name, line.Quantity, null, line.SystemQuantityAtBase,
                line.ExplicitUnitCost, line.AllocationWeight)).ToArray();
            ConversionMetadata? conversion = null;
            if (documentType == InventoryDocumentTypes.Conversion)
            {
                conversion = await BuildConversionMetadataAsync(
                    connection, transaction, user.BusinessId, conversionType!, lines, cancellationToken);
                lines = lines.Select((line, index) => line with
                {
                    ConversionFactor = conversion.Factors[line.ProductId],
                    ConversionEquivalentQuantity = conversion.Equivalence.EquivalentQuantities[index]
                }).ToArray();
                foreach (var input in lines.Where(line => line.Direction == "INPUT"))
                    if (products[input.ProductId].Quantity < input.Quantity)
                        throw new InventoryValidationException($"Insufficient inventory for product '{input.ProductCode}'.");
            }
            var now = timeProvider.GetUtcNow();
            const string insert = """
                INSERT dbo.InventoryOperations
                  (InventoryOperationId,BusinessId,DocumentType,WarehouseId,DestinationWarehouseId,
                   OccurredAt,ReasonCode,ReasonDescription,ConversionType,ConversionFamilyRootProductId,
                   ConversionInputEquivalent,ConversionOutputEquivalent,ConversionLossQuantity,
                   ConversionLossPercent,ConversionMaximumLossPercent,CostCenterId,BaseInventorySequence,Notes,Status,CreatedAt)
                VALUES(@Id,@BusinessId,@Type,@WarehouseId,@Destination,@OccurredAt,@Reason,@ReasonDescription,@ConversionType,
                   @ConversionFamilyRootProductId,@ConversionInputEquivalent,@ConversionOutputEquivalent,@ConversionLossQuantity,
                   @ConversionLossPercent,@ConversionMaximumLossPercent,@CostCenterId,@BaseSequence,@Notes,N'Draft',@Now);
                """;
            await using (var command = new SqlCommand(insert, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", documentId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@Type", documentType);
                command.Parameters.AddWithValue("@WarehouseId", warehouseId);
                command.Parameters.AddWithValue("@Destination", (object?)destinationWarehouseId ?? DBNull.Value);
                command.Parameters.AddWithValue("@OccurredAt", occurredAt);
                command.Parameters.AddWithValue("@Reason", reasonCode);
                command.Parameters.AddWithValue("@ReasonDescription", reasonDescription);
                command.Parameters.AddWithValue("@ConversionType", (object?)conversionType ?? DBNull.Value);
                command.Parameters.AddWithValue("@ConversionFamilyRootProductId", (object?)conversion?.FamilyRootProductId ?? DBNull.Value);
                AddNullableDecimal(command, "@ConversionInputEquivalent", conversion?.Equivalence.InputEquivalent, 19, 6);
                AddNullableDecimal(command, "@ConversionOutputEquivalent", conversion?.Equivalence.OutputEquivalent, 19, 6);
                AddNullableDecimal(command, "@ConversionLossQuantity", conversion?.Equivalence.LossQuantity, 19, 6);
                AddNullableDecimal(command, "@ConversionLossPercent", conversion?.Equivalence.LossPercent, 9, 6);
                AddNullableDecimal(command, "@ConversionMaximumLossPercent", conversion?.MaximumLossPercent, 9, 6);
                command.Parameters.AddWithValue("@CostCenterId", (object?)costCenterId ?? DBNull.Value);
                command.Parameters.AddWithValue("@BaseSequence", (object?)baseSequence ?? DBNull.Value);
                command.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertLinesAsync(connection, transaction, documentId, lines, cancellationToken);
            var acceptance = await AcceptDraftAsync(connection, transaction, user, documentId, documentType,
                warehouseId, destinationWarehouseId, occurredAt, reasonCode, conversionType, costCenterId,
                baseSequence, notes, idempotencyKey, requestHash, lines, conversion, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return acceptance;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InventoryConflictException("The document or idempotency key is already in use.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<InventoryOperationAcceptance> AcceptDraftAsync(
        SqlConnection connection, SqlTransaction transaction, InventoryUserIdentity user,
        Guid documentId, string documentType, Guid warehouseId, Guid? destinationWarehouseId,
        DateTimeOffset occurredAt, string reasonCode, string? conversionType, Guid? costCenterId,
        long? baseSequence, string? notes, string idempotencyKey, byte[] requestHash,
        IReadOnlyList<InventoryOperationLineSnapshot> lines, ConversionMetadata? conversion,
        CancellationToken cancellationToken)
    {
        var number = await AllocateNumberAsync(connection, transaction, user.BusinessId, documentType, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var sequence = await AllocateSequenceAsync(connection, transaction, user.BusinessId, now, cancellationToken);
        var accountingReason = await LoadAccountingReasonAsync(
            connection, transaction, user.BusinessId, documentType, reasonCode,
            costCenterId, notes,
            documentType is InventoryDocumentTypes.StockCount or InventoryDocumentTypes.Adjustment or
                InventoryDocumentTypes.Damage || conversion?.Equivalence.LossQuantity > 0,
            cancellationToken);
        var payload = new InventoryOperationDocumentPayload(user.TenantId, user.BusinessId, documentId,
            documentType, warehouseId, destinationWarehouseId, user.UserId, number.FullNumber,
            number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive, occurredAt,
            reasonCode, conversionType, costCenterId, baseSequence, notes, lines,
            CounterpartAccountingCategory: accountingReason?.CounterpartCategory,
            AccountingCostCenterId: accountingReason?.CostCenterId);
        if (conversion is not null)
            payload = payload with
            {
                ConversionFamilyRootProductId = conversion.FamilyRootProductId,
                ConversionInputEquivalent = conversion.Equivalence.InputEquivalent,
                ConversionOutputEquivalent = conversion.Equivalence.OutputEquivalent,
                ConversionLossQuantity = conversion.Equivalence.LossQuantity,
                ConversionLossPercent = conversion.Equivalence.LossPercent,
                ConversionMaximumLossPercent = conversion.MaximumLossPercent
            };
        var payloadJson = InventoryOperationContractSerializer.Serialize(payload);
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        var movementId = ids.NewId();
        const string sql = """
            UPDATE dbo.InventoryOperations
            SET DocumentSeriesId=@SeriesId,DocumentNumber=@Number,DocumentPrefix=@Prefix,
                DocumentSeriesCode=@SeriesCode,DocumentConsecutive=@Consecutive,
                IdempotencyKey=@IdempotencyKey,PayloadHash=@RequestHash,Status=N'Accepted',
                ConfirmedByUserId=@UserId,AcceptedAt=@Now
            WHERE InventoryOperationId=@DocumentId AND BusinessId=@BusinessId AND Status=N'Draft';
            IF @@ROWCOUNT<>1 THROW 51210,'The inventory operation is not an editable draft.',1;
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,@DocumentType,N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,@DocumentType,@BusinessId,1,@Payload,@PayloadHash,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        command.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@Payload", payloadJson);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new InventoryOperationAcceptance(documentId, movementId, documentType, number.FullNumber, "Accepted", sequence, false);
    }

    private async Task<InventoryOperationAcceptance> AcceptTransferDispatchAsync(
        SqlConnection connection, SqlTransaction transaction, InventoryUserIdentity user, string idempotencyKey,
        byte[] requestHash, DispatchWarehouseTransferRequest request, Guid transitWarehouseId,
        IReadOnlyList<InventoryOperationLineSnapshot> lines, CancellationToken cancellationToken)
    {
        var number = await AllocateNumberAsync(connection, transaction, user.BusinessId, InventoryDocumentTypes.Transfer, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var sequence = await AllocateSequenceAsync(connection, transaction, user.BusinessId, now, cancellationToken);
        var payload = new InventoryOperationDocumentPayload(user.TenantId, user.BusinessId, request.DocumentId,
            InventoryDocumentTypes.TransferDispatch, request.SourceWarehouseId, transitWarehouseId, user.UserId,
            number.FullNumber, number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive,
            request.OccurredAt, request.ReasonCode, null, null, null, request.Notes, lines);
        var payloadJson = InventoryOperationContractSerializer.Serialize(payload);
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        var movementId = ids.NewId();
        const string sql = """
            UPDATE dbo.InventoryOperations SET
              DocumentSeriesId=@SeriesId,DocumentNumber=@Number,DocumentPrefix=@Prefix,DocumentSeriesCode=@SeriesCode,
              DocumentConsecutive=@Consecutive,IdempotencyKey=@Key,PayloadHash=@RequestHash,Status=N'DispatchPending',
              ConfirmedByUserId=@UserId,AcceptedAt=@Now
            WHERE InventoryOperationId=@DocumentId AND BusinessId=@BusinessId AND Status=N'Draft';
            IF @@ROWCOUNT<>1 THROW 51210,'The warehouse transfer is not an editable draft.',1;
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'WarehouseTransferDispatch',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,N'WarehouseTransferDispatch',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        command.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@Payload", payloadJson);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new InventoryOperationAcceptance(request.DocumentId, movementId, InventoryDocumentTypes.TransferDispatch,
            number.FullNumber, "DispatchPending", sequence, false);
    }

    private async Task<InventoryOperationAcceptance> AcceptTransferReceiptAsync(
        SqlConnection connection, SqlTransaction transaction, InventoryUserIdentity user,
        ReceiveWarehouseTransferRequest request, ReceivableTransfer transfer,
        IReadOnlyList<InventoryOperationLineSnapshot> lines,
        AccountingReasonSnapshot? accountingReason,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var sequence = await AllocateSequenceAsync(connection, transaction, user.BusinessId, now, cancellationToken);
        var payload = new InventoryOperationDocumentPayload(user.TenantId, user.BusinessId, request.ReceiptId,
            InventoryDocumentTypes.TransferReceipt, transfer.TransitWarehouseId, transfer.DestinationWarehouseId,
            user.UserId, transfer.DocumentNumber, transfer.DocumentSeriesId, transfer.DocumentPrefix,
            transfer.DocumentSeriesCode, transfer.DocumentConsecutive, request.OccurredAt,
            request.DifferenceReasonCode ?? transfer.ReasonCode, null, null, null, request.Notes, lines,
            CounterpartAccountingCategory: accountingReason?.CounterpartCategory,
            AccountingCostCenterId: accountingReason?.CostCenterId);
        var payloadJson = InventoryOperationContractSerializer.Serialize(payload);
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        var movementId = ids.NewId();
        const string sql = """
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@ReceiptId,N'WarehouseTransferReceipt',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@ReceiptId,N'WarehouseTransferReceipt',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@ReceiptId", request.ReceiptId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", payloadJson);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new InventoryOperationAcceptance(request.ReceiptId, movementId, InventoryDocumentTypes.TransferReceipt,
            transfer.DocumentNumber, "ReceiptPending", sequence, false);
    }

    private static async Task<InventoryOperationAcceptance?> TryTransferStageReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid documentId, string stageType,
        string idempotencyKey, byte[] requestHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT o.InventoryOperationId,o.DocumentNumber,o.Status,o.PayloadHash,j.ProcessingSequence,j.JobId
            FROM dbo.InventoryOperations o WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=o.InventoryOperationId AND j.DocumentType=@StageType
            WHERE o.BusinessId=@BusinessId AND (o.InventoryOperationId=@DocumentId OR o.IdempotencyKey=@Key) AND o.Status<>N'Draft';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        command.Parameters.AddWithValue("@StageType", stageType);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(requestHash))
            throw new InventoryConflictException("The idempotency key or DocumentId was reused with another payload.");
        return new InventoryOperationAcceptance(reader.GetGuid(0), reader.GetGuid(5), stageType, reader.GetString(1),
            reader.GetString(2), reader.GetInt64(4), true);
    }

    private static async Task<InventoryOperationAcceptance?> TryReceiptReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid receiptId,
        string idempotencyKey, byte[] requestHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.InventoryTransferReceiptId,o.DocumentNumber,r.Status,r.RequestHash,j.ProcessingSequence,j.JobId
            FROM dbo.InventoryTransferReceipts r WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.InventoryOperations o ON o.InventoryOperationId=r.TransferId
            INNER JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=r.InventoryTransferReceiptId AND j.DocumentType=N'WarehouseTransferReceipt'
            WHERE r.BusinessId=@BusinessId AND (r.InventoryTransferReceiptId=@ReceiptId OR r.IdempotencyKey=@Key);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ReceiptId", receiptId);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(requestHash))
            throw new InventoryConflictException("The receipt idempotency key or ReceiptId was reused with another payload.");
        return new InventoryOperationAcceptance(reader.GetGuid(0), reader.GetGuid(5), InventoryDocumentTypes.TransferReceipt,
            reader.GetString(1), reader.GetString(2), reader.GetInt64(4), true);
    }

    private static async Task<Guid> LoadTransitWarehouseAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT WarehouseId FROM dbo.Warehouses WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId AND Code=N'TRA' AND IsSystem=1 AND IsActive=1;";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid value ? value : throw new InventoryValidationException("The inventory transit warehouse is not provisioned for this business.");
    }

    private static async Task<ReceivableTransfer> LoadReceivableTransferAsync(
        SqlConnection connection, SqlTransaction transaction, InventoryUserIdentity user, Guid transferId,
        byte[] rowVersion, CancellationToken cancellationToken)
    {
        const string headerSql = """
            SELECT o.DestinationWarehouseId,o.TransitWarehouseId,o.DocumentNumber,o.DocumentSeriesId,o.DocumentPrefix,
                   o.DocumentSeriesCode,o.DocumentConsecutive,o.ReasonCode,o.RowVersion
            FROM dbo.InventoryOperations o WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
            WHERE o.InventoryOperationId=@Id AND o.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND o.DocumentType=N'WarehouseTransfer' AND o.TransferMode=N'DispatchAndReceive'
              AND o.Status IN(N'Dispatched',N'PartiallyReceived');
            """;
        Guid destination; Guid transit; string number; Guid series; string prefix; string seriesCode; long consecutive; string reason;
        await using (var command = new SqlCommand(headerSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", transferId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InventoryValidationException("The transfer is not available for receipt.");
            if (!reader.GetFieldValue<byte[]>(8).AsSpan().SequenceEqual(rowVersion))
                throw new InventoryConflictException("The transfer changed after it was loaded. Reload it before confirming receipt.");
            destination = reader.GetGuid(0); transit = reader.GetGuid(1); number = reader.GetString(2);
            series = reader.GetGuid(3); prefix = reader.GetString(4); seriesCode = reader.GetString(5);
            consecutive = reader.GetInt64(6); reason = reader.GetString(7);
        }
        const string linesSql = """
            SELECT LineNumber,ProductId,ProductCodeSnapshot,DescriptionSnapshot,
                   DispatchedQuantity,ReceivedQuantity,COALESCE(LostQuantity,0),DispatchUnitCost
            FROM dbo.InventoryOperationLines WITH(UPDLOCK,HOLDLOCK)
            WHERE InventoryOperationId=@Id ORDER BY LineNumber;
            """;
        var lines = new List<ReceivableTransferLine>();
        await using (var command = new SqlCommand(linesSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", transferId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var dispatched = reader.GetDecimal(4);
                var received = reader.GetDecimal(5);
                var lost = reader.GetDecimal(6);
                lines.Add(new ReceivableTransferLine(reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), dispatched, received, lost, dispatched - received - lost, reader.GetDecimal(7)));
            }
        }
        return new ReceivableTransfer(destination, transit, number, series, prefix, seriesCode, consecutive, reason, lines);
    }

    private static async Task<InventoryOperationAcceptance?> TryReplayAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid documentId, string idempotencyKey, byte[] requestHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT o.InventoryOperationId,o.DocumentType,o.DocumentNumber,o.Status,o.PayloadHash,j.ProcessingSequence,j.JobId
            FROM dbo.InventoryOperations o WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=o.InventoryOperationId AND j.DocumentType=o.DocumentType
            WHERE o.BusinessId=@BusinessId AND (o.InventoryOperationId=@DocumentId OR o.IdempotencyKey=@Key) AND o.Status<>N'Draft';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(4).AsSpan().SequenceEqual(requestHash))
            throw new InventoryConflictException("The idempotency key or DocumentId was reused with another payload.");
        return new InventoryOperationAcceptance(reader.GetGuid(0), reader.GetGuid(6), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(5), true);
    }

    private static async Task ValidateScopeAsync(SqlConnection connection, SqlTransaction transaction,
        InventoryUserIdentity user, string documentType, Guid warehouseId, Guid? destinationWarehouseId,
        IEnumerable<Guid> productIds, CancellationToken cancellationToken,
        bool allowSystemWarehouses = false)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51200,'The business is outside the authenticated tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId
              AND IsActive=1 AND (IsSystem=0 OR @AllowSystemWarehouses=1))
              THROW 51201,'Selecciona una bodega de inventario activa.',1;
            IF @Destination IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@Destination AND BusinessId=@BusinessId
              AND IsActive=1 AND (IsSystem=0 OR @AllowSystemWarehouses=1 OR (@DocumentType=N'Damage' AND IsSystem=1 AND Code=N'AVE')))
              THROW 51202,'Selecciona una bodega de inventario de destino activa.',1;
            IF EXISTS(SELECT x.ProductId FROM OPENJSON(@Products) WITH(ProductId UNIQUEIDENTIFIER '$') x
              LEFT JOIN dbo.Products p ON p.ProductId=x.ProductId AND p.BusinessId=@BusinessId AND p.IsActive=1 AND p.ManageStock=1
              WHERE p.ProductId IS NULL)
              THROW 51203,'Every product must be active, belong to the business and manage stock.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@Destination", (object?)destinationWarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@AllowSystemWarehouses", allowSystemWarehouses);
        command.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(productIds.Distinct()));
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number is >= 51200 and <= 51203) { throw new InventoryValidationException(exception.Message); }
    }

    private static async Task<Guid> LoadSystemWarehouseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string code,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT WarehouseId FROM dbo.Warehouses WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId AND Code=@Code AND IsSystem=1 AND IsActive=1;";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Code", code);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid warehouseId
            ? warehouseId
            : throw new InventoryValidationException($"La bodega interna '{code}' no está aprovisionada.");
    }

    private static async Task<string> LoadActiveReasonAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, string operationType, string reasonCode, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Name FROM dbo.BusinessReasons WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId AND ReasonType=@OperationType AND Code=@Code AND IsActive=1;";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@OperationType", operationType);
        command.Parameters.AddWithValue("@Code", reasonCode);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string ?? throw new InventoryValidationException("Select an active reason compatible with the inventory operation.");
    }

    private static async Task<AccountingReasonSnapshot?> LoadAccountingReasonAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string operationType,
        string reasonCode,
        Guid? requestedCostCenterId,
        string? reference,
        bool accountingRequired,
        CancellationToken cancellationToken)
    {
        if (!accountingRequired)
            return null;

        const string sql = """
            SELECT r.CounterpartAccountingCategory,
                   COALESCE(@RequestedCostCenterId,r.DefaultCostCenterId),
                   r.RequiresReference,
                   CAST(CASE WHEN COALESCE(@RequestedCostCenterId,r.DefaultCostCenterId) IS NULL
                                  OR EXISTS
                                  (
                                    SELECT 1 FROM dbo.AccountingCostCenters c
                                    WHERE c.CostCenterId=COALESCE(@RequestedCostCenterId,r.DefaultCostCenterId)
                                      AND c.BusinessId=r.BusinessId AND c.IsActive=1
                                  )
                             THEN 1 ELSE 0 END AS bit)
            FROM dbo.BusinessReasons r WITH(UPDLOCK,HOLDLOCK)
            WHERE r.BusinessId=@BusinessId AND r.ReasonType=@OperationType
              AND r.Code=@Code AND r.IsActive=1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@OperationType", operationType);
        command.Parameters.AddWithValue("@Code", reasonCode);
        command.Parameters.AddWithValue("@RequestedCostCenterId", (object?)requestedCostCenterId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InventoryValidationException("Select an active reason compatible with the inventory operation.");
        var counterpart = reader.IsDBNull(0) ? null : reader.GetString(0);
        Guid? costCenterId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        var requiresReference = reader.GetBoolean(2);
        var validCostCenter = reader.GetBoolean(3);
        if (string.IsNullOrWhiteSpace(counterpart))
            throw new InventoryValidationException(
                "The inventory reason does not have an accounting counterpart category configured.");
        if (!validCostCenter)
            throw new InventoryValidationException(
                "The accounting cost center must be active and belong to the business.");
        if (requiresReference && string.IsNullOrWhiteSpace(reference))
            throw new InventoryValidationException(
                "A reference or supporting note is required for the selected inventory reason.");
        return new AccountingReasonSnapshot(counterpart.Trim(), costCenterId);
    }

    private static async Task<IReadOnlyList<ProductState>> LoadProductsAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid warehouseId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.ProductId,COALESCE(p.ProductCode,p.Sku),p.Name,
                   COALESCE(b.QuantityOnHand / NULLIF(CASE WHEN link.ProductLinkId IS NULL THEN 1 ELSE link.InventoryFactor END,0),0)
            FROM dbo.Products p WITH (UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.ProductLinks link WITH (UPDLOCK,HOLDLOCK)
              ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            INNER JOIN dbo.Products inventoryProduct WITH (UPDLOCK,HOLDLOCK)
              ON inventoryProduct.ProductId=COALESCE(link.ParentProductId,p.ProductId) AND inventoryProduct.BusinessId=p.BusinessId
            LEFT JOIN dbo.InventoryBalances b WITH (UPDLOCK,HOLDLOCK)
              ON b.BusinessId=p.BusinessId AND b.WarehouseId=@WarehouseId AND b.ProductId=inventoryProduct.ProductId
            INNER JOIN OPENJSON(@Products) WITH(ProductId UNIQUEIDENTIFIER '$') x ON x.ProductId=p.ProductId
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND inventoryProduct.ManageStock=1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(productIds.Distinct()));
        var result = new List<ProductState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3)));
        return result;
    }

    private static async Task<ConversionMetadata> BuildConversionMetadataAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string conversionType,
        IReadOnlyList<InventoryOperationLineSnapshot> lines,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.ProductId,
                   COALESCE(link.ParentProductId,p.ProductId) AS FamilyRootProductId,
                   CAST(CASE WHEN link.ProductLinkId IS NULL THEN 1 ELSE link.ConversionFactor END AS decimal(19,6)) AS ConversionFactor,
                   root.ConversionMaximumLossPercent
            FROM dbo.Products p WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN OPENJSON(@Products) WITH(ProductId uniqueidentifier '$') requested ON requested.ProductId=p.ProductId
            LEFT JOIN dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK)
              ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId
             AND link.IsActive=1 AND link.AllowsConversion=1
            INNER JOIN dbo.Products root WITH(UPDLOCK,HOLDLOCK)
              ON root.BusinessId=p.BusinessId AND root.ProductId=COALESCE(link.ParentProductId,p.ProductId)
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND p.ManageStock=1
              AND root.IsActive=1 AND root.ManageStock=1
              AND root.ConversionMaximumLossPercent IS NOT NULL
              AND (link.ProductLinkId IS NOT NULL OR EXISTS(
                    SELECT 1 FROM dbo.ProductLinks child WITH(UPDLOCK,HOLDLOCK)
                    INNER JOIN dbo.Products childProduct WITH(UPDLOCK,HOLDLOCK)
                      ON childProduct.ProductId=child.ChildProductId AND childProduct.BusinessId=child.BusinessId
                     AND childProduct.IsActive=1 AND childProduct.ManageStock=1
                    WHERE child.BusinessId=p.BusinessId AND child.ParentProductId=p.ProductId
                      AND child.IsActive=1 AND child.AllowsConversion=1));
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(lines.Select(line => line.ProductId).Distinct()));
        var factors = new Dictionary<Guid, decimal>();
        Guid? rootProductId = null;
        decimal? maximumLossPercent = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var productId = reader.GetGuid(0);
                var rowRootProductId = reader.GetGuid(1);
                if (rootProductId is not null && rootProductId != rowRootProductId)
                    throw new InventoryValidationException("All conversion products must belong to the same linked family.");
                rootProductId = rowRootProductId;
                factors[productId] = reader.GetDecimal(2);
                maximumLossPercent = reader.GetDecimal(3);
            }
        }
        if (factors.Count != lines.Select(line => line.ProductId).Distinct().Count() || rootProductId is null || maximumLossPercent is null)
            throw new InventoryValidationException("Every conversion product must be enabled in the same linked product family.");

        try
        {
            var equivalence = InventoryOperationRules.ValidateConversionEquivalence(
                conversionType,
                lines.Select(line => (line.Direction, line.Quantity, factors[line.ProductId])).ToArray(),
                maximumLossPercent.Value);
            return new ConversionMetadata(rootProductId.Value, maximumLossPercent.Value, factors, equivalence);
        }
        catch (ArgumentException exception)
        {
            throw new InventoryValidationException(exception.Message);
        }
    }

    private static async Task InsertLinesAsync(SqlConnection connection, SqlTransaction transaction, Guid documentId,
        IEnumerable<InventoryOperationLineSnapshot> lines, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.InventoryOperationLines
              (InventoryOperationId,LineNumber,Direction,ProductId,ProductCodeSnapshot,DescriptionSnapshot,
               Quantity,PreCountQuantity,SystemQuantityAtBase,ExplicitUnitCost,AllocationWeight,
               ConversionFactor,ConversionEquivalentQuantity,DispatchedQuantity,ReceivedQuantity,LostQuantity,DispatchUnitCost)
            VALUES(@Id,@Line,@Direction,@ProductId,@Code,@Description,@Quantity,@PreCount,@SystemAtBase,@UnitCost,@Weight,
               @ConversionFactor,@ConversionEquivalentQuantity,@DispatchedQuantity,@ReceivedQuantity,@LostQuantity,@DispatchUnitCost);
            """;
        foreach (var line in lines)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Id", documentId);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            command.Parameters.AddWithValue("@Direction", line.Direction);
            command.Parameters.AddWithValue("@ProductId", line.ProductId);
            command.Parameters.AddWithValue("@Code", line.ProductCode);
            command.Parameters.AddWithValue("@Description", line.Description);
            AddNullableDecimal(command, "@Quantity", line.Direction == "COUNT" ? null : line.Quantity, 19, 6);
            AddNullableDecimal(command, "@PreCount", line.PreCountQuantity, 19, 6);
            AddNullableDecimal(command, "@SystemAtBase", line.SystemQuantityAtBase, 19, 6);
            AddNullableDecimal(command, "@UnitCost", line.ExplicitUnitCost, 19, 6);
            AddNullableDecimal(command, "@Weight", line.AllocationWeight, 9, 6);
            AddNullableDecimal(command, "@ConversionFactor", line.ConversionFactor, 19, 6);
            AddNullableDecimal(command, "@ConversionEquivalentQuantity", line.ConversionEquivalentQuantity, 19, 6);
            AddNullableDecimal(command, "@DispatchedQuantity", line.DispatchedQuantity, 19, 6);
            AddNullableDecimal(command, "@ReceivedQuantity", line.ReceivedQuantity, 19, 6);
            AddNullableDecimal(command, "@LostQuantity", line.TransferLossQuantity, 19, 6);
            AddNullableDecimal(command, "@DispatchUnitCost", line.DispatchUnitCost, 19, 6);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<CountDraftState> LoadCountDraftAsync(SqlConnection connection, SqlTransaction transaction,
        InventoryUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        const string header = """
            SELECT o.WarehouseId,o.OccurredAt,o.ReasonCode,o.BaseInventorySequence,o.Notes
            FROM dbo.InventoryOperations o WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
            WHERE o.InventoryOperationId=@Id AND o.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND o.DocumentType=N'StockCount' AND o.Status=N'Draft';
            """;
        Guid warehouseId; DateTimeOffset occurred; string reason; long sequence; string? notes;
        await using (var command = new SqlCommand(header, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", documentId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InventoryValidationException("The stock count draft was not found.");
            warehouseId=reader.GetGuid(0); occurred=reader.GetDateTimeOffset(1); reason=reader.GetString(2); sequence=reader.GetInt64(3); notes=reader.IsDBNull(4)?null:reader.GetString(4);
        }
        const string detail = "SELECT LineNumber,ProductId,ProductCodeSnapshot,DescriptionSnapshot,PreCountQuantity,SystemQuantityAtBase FROM dbo.InventoryOperationLines WHERE InventoryOperationId=@Id ORDER BY LineNumber;";
        var lines = new List<InventoryOperationLineSnapshot>();
        await using (var command = new SqlCommand(detail, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", documentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while(await reader.ReadAsync(cancellationToken))
            {
                var systemQuantityAtBase = reader.GetDecimal(5);
                var preCountQuantity = reader.IsDBNull(4) ? systemQuantityAtBase : reader.GetDecimal(4);
                lines.Add(new(reader.GetInt32(0),"COUNT",reader.GetGuid(1),reader.GetString(2),reader.GetString(3),0m,preCountQuantity,systemQuantityAtBase,null,null));
            }
        }
        return new(warehouseId,occurred,reason,sequence,notes,lines);
    }

    private static async Task<long> ReadBaseSequenceAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COALESCE(LastAssignedSequence,0) FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId;";
        await using var command = new SqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("@BusinessId", businessId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static async Task RequireCurrentProcessingSequenceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var command = StoredProcedure("dbo.InventoryProcessingSequenceRequireCurrent", connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number == 51220)
        { throw new InventoryConflictException(exception.Message); }
    }

    private async Task ProcessAcceptedInsideTransactionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InventoryUserIdentity user,
        InventoryOperationAcceptance acceptance,
        CancellationToken cancellationToken)
    {
        string payload;
        DateTimeOffset acceptedAt;
        await using (var command = StoredProcedure("dbo.DocumentProcessingPayloadGet", connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", acceptance.DocumentId);
            command.Parameters.AddWithValue("@DocumentType", acceptance.DocumentType);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InventoryConflictException("No se pudo recuperar la reserva aceptada.");
            payload = reader.GetString(0);
            acceptedAt = reader.GetDateTimeOffset(1);
        }

        var context = new DocumentProcessingContext(
            new TenantId(user.TenantId),
            new BusinessId(user.BusinessId),
            new DocumentId(acceptance.DocumentId),
            acceptance.DocumentType);
        processingSessions.Set(connection, transaction, context, acceptance.MovementId, acceptance.ProcessingSequence);
        try
        {
            await processor.HandleAsync(new ConfirmedDocument(
                context.TenantId,
                context.BusinessId,
                context.DocumentId,
                context.DocumentType,
                payload,
                acceptedAt), cancellationToken);
        }
        finally
        {
            processingSessions.Take();
        }

        await using var complete = StoredProcedure("dbo.DocumentProcessingComplete", connection, transaction);
        complete.Parameters.AddWithValue("@JobId", acceptance.MovementId);
        complete.Parameters.AddWithValue("@Sequence", acceptance.ProcessingSequence);
        complete.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        complete.Parameters.AddWithValue("@CompletedAt", timeProvider.GetUtcNow());
        try { await complete.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number is 51221 or 51222)
        { throw new DBConcurrencyException("La reserva y su secuencia no pudieron confirmarse atómicamente.", exception); }
    }

    private static async Task<long> AllocateSequenceAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt) VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
            OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("@BusinessId", businessId); command.Parameters.AddWithValue("@Now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, string documentType, CancellationToken cancellationToken)
    {
        var defaultPrefix = AuralyDocumentTypes.DefaultPrefix(documentType);
        const string ensureSql = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.DocumentSeries WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND DocumentType=@Type AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries
                (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,NULL,@Type,@Prefix,N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        await using (var ensure = new SqlCommand(ensureSql, connection, transaction))
        {
            ensure.Parameters.AddWithValue("@BusinessId", businessId);
            ensure.Parameters.AddWithValue("@Type", documentType);
            ensure.Parameters.AddWithValue("@Prefix", defaultPrefix);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }        const string selectSql = """
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK) ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=@Type AND ds.DeviceId IS NULL AND ds.IsActive=1 ORDER BY ds.DocumentSeriesId;
            """;
        Guid seriesId; string prefix; string code; byte padding; long rangeEnd; long consecutive;
        await using (var command = new SqlCommand(selectSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@BusinessId", businessId); command.Parameters.AddWithValue("@Type", documentType);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if(!await reader.ReadAsync(cancellationToken)) throw new InventoryValidationException("La serie de este movimiento de inventario no está activa para la sede.");
            seriesId=reader.GetGuid(0); prefix=reader.GetString(1); code=reader.GetString(2); padding=reader.GetByte(3); rangeEnd=reader.GetInt64(4); consecutive=reader.GetInt64(5);
        }
        if(consecutive>rangeEnd) throw new InventoryValidationException("La numeración de este movimiento de inventario se agotó.");
        const string update="""
            IF EXISTS(SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@Id)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@Id;
            ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt) VALUES(@Id,@Next,@Now);
            """;
        await using var updateCommand=new SqlCommand(update,connection,transaction); updateCommand.Parameters.AddWithValue("@Id",seriesId); updateCommand.Parameters.AddWithValue("@Next",consecutive+1); updateCommand.Parameters.AddWithValue("@Now",DateTimeOffset.UtcNow); await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(seriesId,documentType,prefix,code,consecutive,padding);
    }

    private static byte[] Hash(object value) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value));
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=precision;p.Scale=scale;p.Value=value;}
    private static void AddNullableDecimal(SqlCommand command,string name,decimal? value,byte precision,byte scale){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=precision;p.Scale=scale;p.Value=(object?)value??DBNull.Value;}
    private static SqlCommand StoredProcedure(string name,SqlConnection connection,SqlTransaction transaction)=>new(name,connection,transaction){CommandType=CommandType.StoredProcedure};
    private sealed record LineInput(int LineNumber,string Direction,Guid ProductId,decimal Quantity,decimal? SystemQuantityAtBase,decimal? ExplicitUnitCost,decimal? AllocationWeight);
    private sealed record ProductState(Guid Id,string Code,string Name,decimal Quantity);
    private sealed record AccountingReasonSnapshot(string CounterpartCategory, Guid? CostCenterId);
    private sealed record CountDraftState(Guid WarehouseId,DateTimeOffset OccurredAt,string ReasonCode,long BaseSequence,string? Notes,IReadOnlyList<InventoryOperationLineSnapshot> Lines);
    private sealed record ReceivableTransfer(
        Guid DestinationWarehouseId,
        Guid TransitWarehouseId,
        string DocumentNumber,
        Guid DocumentSeriesId,
        string DocumentPrefix,
        string DocumentSeriesCode,
        long DocumentConsecutive,
        string ReasonCode,
        IReadOnlyList<ReceivableTransferLine> Lines);
    private sealed record ReceivableTransferLine(
        int LineNumber,
        Guid ProductId,
        string ProductCode,
        string ProductName,
        decimal DispatchedQuantity,
        decimal ReceivedQuantity,
        decimal LostQuantity,
        decimal PendingQuantity,
        decimal DispatchUnitCost);
    private sealed record ConversionMetadata(
        Guid FamilyRootProductId,
        decimal MaximumLossPercent,
        IReadOnlyDictionary<Guid, decimal> Factors,
        ProductConversionEquivalence Equivalence);
}
