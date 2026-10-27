using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlGoodsReceiptStore
{
    public async Task<GoodsReceiptCostRevisionAcceptance> AcceptCostRevisionAsync(
        PurchasingUserIdentity user, string idempotencyKey, byte[] commandHash,
        ReviseGoodsReceiptCostsRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptCostRevisionAttemptAsync(user, idempotencyKey,
                    commandHash, request, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<GoodsReceiptCostRevisionAcceptance> AcceptCostRevisionAttemptAsync(
        PurchasingUserIdentity user, string idempotencyKey, byte[] commandHash,
        ReviseGoodsReceiptCostsRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await LoadCostRevisionReplayAsync(connection, transaction, user,
                idempotencyKey, commandHash, request, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            GoodsReceiptDocumentPayload receipt;
            string accountingStatus;
            await using (var command = new SqlCommand("""
                SELECT payload.PayloadJson,posting.Status
                FROM dbo.GoodsReceipts receipt WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.Businesses business ON business.BusinessId=receipt.BusinessId
                  AND business.TenantId=@TenantId
                JOIN dbo.DocumentProcessingPayloads payload
                  ON payload.DocumentId=receipt.GoodsReceiptId
                 AND payload.DocumentType=N'GoodsReceipt' AND payload.BusinessId=receipt.BusinessId
                LEFT JOIN dbo.AccountingPostingJobs posting
                  ON posting.SourceDocumentId=receipt.GoodsReceiptId
                 AND posting.SourceDocumentType=N'GoodsReceipt' AND posting.BusinessId=receipt.BusinessId
                WHERE receipt.GoodsReceiptId=@ReceiptId AND receipt.BusinessId=@BusinessId
                  AND receipt.Status=N'Processed';
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@ReceiptId", request.GoodsReceiptId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new PurchasingValidationException(
                        "La recepción no está procesada o no pertenece a esta sede.");
                receipt = GoodsReceiptContractSerializer.Deserialize(reader.GetString(0));
                accountingStatus = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            }
            if (receipt.TenantId != user.TenantId || receipt.BusinessId != user.BusinessId ||
                receipt.DocumentId != request.GoodsReceiptId)
                throw new InvalidOperationException("La fuente inmutable de compra no coincide con la recepción.");
            var distributes = request.ReferencedCostDocuments.Any(document =>
                document.Mode == GoodsReceiptCostAssociationModes.Distribute);
            if (distributes && accountingStatus != "Posted")
                throw new PurchasingConflictException(
                    "La compra aún no está contabilizada. Revisa su estado contable antes de repartir un cargo.");

            var calculated = GoodsReceiptCostCalculator.CalculateReferenced(
                request.ReferencedCostDocuments, receipt.Lines);
            var now = timeProvider.GetUtcNow();
            var snapshots = await ResolveReferencedCostsAsync(connection, transaction, user,
                calculated, now, cancellationToken) ?? [];
            var revisionNumber = await NextCostRevisionNumberAndRequireOriginsAsync(connection,
                transaction, user.BusinessId, request.GoodsReceiptId,
                distributes ? calculated.SelectMany(document => document.Allocations)
                    .Where(allocation => allocation.FunctionalAmount > 0m)
                    .Select(allocation => allocation.ReceiptLineNumber).Distinct().ToArray() : [],
                cancellationToken);
            var payload = new GoodsReceiptCostRevisionPayload(user.TenantId, user.BusinessId,
                request.RevisionId, request.GoodsReceiptId, revisionNumber, receipt.WarehouseId,
                receipt.SupplierId, user.UserId, receipt.DocumentNumber, now,
                receipt.Lines, snapshots);
            var jobId = distributes ? ids.NewId() : (Guid?)null;
            await using (var command = new SqlCommand("""
                INSERT purchasing.GoodsReceiptCostRevisions
                  (RevisionId,GoodsReceiptId,BusinessId,RevisionNumber,IdempotencyKey,
                   RequestHash,Status,AcceptedByUserId,AcceptedAt,ProcessedAt)
                VALUES(@RevisionId,@ReceiptId,@BusinessId,@RevisionNumber,@IdempotencyKey,
                  @RequestHash,@Status,@UserId,@Now,@ProcessedAt);
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@RevisionId", request.RevisionId);
                command.Parameters.AddWithValue("@ReceiptId", request.GoodsReceiptId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@RevisionNumber", revisionNumber);
                command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
                command.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = commandHash;
                command.Parameters.AddWithValue("@Status", distributes ? "Accepted" : "Processed");
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.AddWithValue("@ProcessedAt", distributes ? DBNull.Value : now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertCostDocumentsAsync(connection, transaction, request.GoodsReceiptId,
                [], snapshots, now, cancellationToken, request.RevisionId);
            if (jobId is { } scheduledJob)
            {
                var payloadJson = GoodsReceiptContractSerializer.SerializeCostRevision(payload);
                await InsertJobAsync(connection, transaction, user.BusinessId, request.RevisionId,
                    PurchasingDocumentTypes.GoodsReceiptCostRevision, scheduledJob,
                    await AllocateProcessingSequenceAsync(connection, transaction, user.BusinessId,
                        now, cancellationToken), payloadJson,
                    SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)), now, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new(request.RevisionId, request.GoodsReceiptId, revisionNumber,
                jobId, distributes ? "Accepted" : "Processed", false);
        }
        catch (Exception exception)
        {
            if (transaction.Connection?.State == ConnectionState.Open)
                await transaction.RollbackAsync(CancellationToken.None);
            if (exception is SqlException { Number: 2601 or 2627 })
                throw new PurchasingConflictException(
                    "La revisión, el comprobante o la clave de confirmación ya están en uso.", exception);
            throw;
        }
    }

    private static async Task<GoodsReceiptCostRevisionAcceptance?> LoadCostRevisionReplayAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        string idempotencyKey, byte[] commandHash, ReviseGoodsReceiptCostsRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT revision.RevisionId,revision.GoodsReceiptId,revision.RevisionNumber,
              revision.RequestHash,revision.Status,job.JobId
            FROM purchasing.GoodsReceiptCostRevisions revision WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business ON business.BusinessId=revision.BusinessId
              AND business.TenantId=@TenantId
            LEFT JOIN dbo.DocumentProcessingJobs job ON job.DocumentId=revision.RevisionId
              AND job.DocumentType=N'GoodsReceiptCostRevision' AND job.BusinessId=revision.BusinessId
            WHERE revision.BusinessId=@BusinessId AND
              (revision.RevisionId=@RevisionId OR revision.IdempotencyKey=@IdempotencyKey);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@RevisionId", request.RevisionId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var storedId = reader.GetGuid(0);
        var receiptId = reader.GetGuid(1);
        var number = reader.GetInt32(2);
        var storedHash = reader.GetFieldValue<byte[]>(3);
        var status = reader.GetString(4);
        var jobId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
        if (await reader.ReadAsync(cancellationToken) || storedId != request.RevisionId ||
            receiptId != request.GoodsReceiptId || !storedHash.AsSpan().SequenceEqual(commandHash))
            throw new PurchasingConflictException(
                "Esta revisión o clave ya se usó con otros datos. Recarga la recepción.");
        if (status == "Accepted" && jobId is null)
            throw new InvalidOperationException("La revisión aceptada no conserva su trabajo documental.");
        return new(storedId, receiptId, number, jobId, status, true);
    }

    // Fail before reserving a source voucher when a legacy receipt cannot support
    // the adjustment. The document handler rechecks the live quantities when it posts.
    private static async Task<int> NextCostRevisionNumberAndRequireOriginsAsync(SqlConnection connection,
        SqlTransaction transaction, Guid businessId, Guid receiptId,
        IReadOnlyList<int> affectedLines, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Affected TABLE(LineNumber int PRIMARY KEY);
            INSERT @Affected SELECT [value] FROM OPENJSON(@LinesJson) WITH ([value] int '$');
            SELECT CONVERT(bit,CASE WHEN EXISTS(
              SELECT 1 FROM @Affected affected
              JOIN dbo.GoodsReceiptLines line ON line.GoodsReceiptId=@ReceiptId
                AND line.LineNumber=affected.LineNumber
              JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
              JOIN dbo.Products product ON product.ProductId=line.ProductId
                AND product.TenantId=business.TenantId
              LEFT JOIN dbo.ProductLinks link ON link.ChildProductId=product.ProductId
                AND link.TenantId=business.TenantId AND link.SharesInventory=1 AND link.IsActive=1
              JOIN dbo.Products inventoryProduct
                ON inventoryProduct.ProductId=COALESCE(link.ParentProductId,product.ProductId)
                AND inventoryProduct.TenantId=business.TenantId
              LEFT JOIN dbo.InventoryMovements movement
                ON movement.DocumentId=@ReceiptId AND movement.DocumentType=N'GoodsReceipt'
               AND movement.BusinessId=@BusinessId AND movement.LineNumber=line.LineNumber
               AND movement.MovementType=N'GoodsReceipt'
               AND movement.ProductId=inventoryProduct.ProductId
              WHERE inventoryProduct.ManageStock=1 AND
                (movement.InventoryMovementId IS NULL OR NOT EXISTS(
                  SELECT 1 FROM inventory.MovementOrigins origin
                  WHERE origin.InventoryMovementId=movement.InventoryMovementId))
            ) OR EXISTS(
              SELECT 1 FROM @Affected affected
              JOIN dbo.InventoryMovements movement ON movement.DocumentId=@ReceiptId
                AND movement.DocumentType=N'GoodsReceipt' AND movement.BusinessId=@BusinessId
                AND movement.LineNumber=affected.LineNumber AND movement.MovementType=N'GoodsReceipt'
              JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
                AND balance.ProductId=movement.ProductId AND balance.QuantityOnHand>0
              WHERE balance.OriginContextId IS NULL OR
                balance.OriginSnapshotSequence<>balance.LastProcessingSequence
            ) THEN 1 ELSE 0 END);
            SELECT COALESCE(MAX(RevisionNumber),0)+1
            FROM purchasing.GoodsReceiptCostRevisions WITH(UPDLOCK,HOLDLOCK)
            WHERE GoodsReceiptId=@ReceiptId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ReceiptId", receiptId);
        command.Parameters.Add("@LinesJson", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(affectedLines);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("La revisión no devolvió su estado de origen.");
        var lacksOrigins = reader.GetBoolean(0);
        await reader.NextResultAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("La revisión no devolvió su consecutivo.");
        var number = reader.GetInt32(0);
        if (lacksOrigins)
            throw new PurchasingConflictException(
                "Esta recepción no conserva el origen de inventario necesario para actualizar su costo con seguridad.");
        return number;
    }
}
