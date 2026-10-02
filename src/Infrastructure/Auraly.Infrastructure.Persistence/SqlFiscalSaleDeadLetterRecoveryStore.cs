using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal sealed record SaleDeadLetterRecoveryPayload(
    Guid DocumentId,
    Guid BusinessId,
    Guid RequestedByUserId,
    string Reason,
    string OriginalPayloadHash);

public sealed class SqlFiscalSaleDeadLetterRecoveryStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time,
    IDocumentProcessingSignalPublisher signals) : IFiscalSaleDeadLetterRecoveryStore
{
    public async Task<DeadLetteredSaleRecoveryResult> AcceptAsync(
        FiscalUserIdentity user, Guid documentId, string reason,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var state = new SqlCommand("""
                SELECT original.JobId,original.Status,d.ProcessingStatus,
                       payload.PayloadHash,original.LastError,
                       recovery.JobId,recovery.ProcessingSequence,recovery.Status
                FROM dbo.SalesDocuments d WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.Businesses business ON business.BusinessId=d.BusinessId
                JOIN dbo.AppUsers operatorUser ON operatorUser.UserId=@UserId
                  AND operatorUser.TenantId=business.TenantId AND operatorUser.IsActive=1
                JOIN dbo.DocumentProcessingJobs original WITH(UPDLOCK,HOLDLOCK)
                  ON original.DocumentId=d.DocumentId AND original.DocumentType=N'SalesInvoice'
                JOIN dbo.DocumentProcessingPayloads payload WITH(UPDLOCK,HOLDLOCK)
                  ON payload.DocumentId=d.DocumentId AND payload.DocumentType=N'SalesInvoice'
                JOIN dbo.FiscalSnapshots snapshot ON snapshot.DocumentId=d.DocumentId
                  AND snapshot.IntegrityStatus=N'FiscalVerified'
                  AND snapshot.PayloadHash=d.PayloadHash
                LEFT JOIN dbo.DocumentProcessingJobs recovery WITH(UPDLOCK,HOLDLOCK)
                  ON recovery.DocumentId=d.DocumentId AND recovery.DocumentType=@RecoveryType
                WHERE d.DocumentId=@DocumentId AND d.BusinessId=@BusinessId
                  AND d.DocumentType=N'SalesInvoice'
                  AND d.FiscalStatus IN(N'FiscalVerified',N'DianAccepted')
                  AND payload.BusinessId=d.BusinessId
                  AND payload.PayloadHash=d.PayloadHash;
                """, connection, transaction);
            state.Parameters.AddWithValue("@DocumentId", documentId);
            state.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            state.Parameters.AddWithValue("@UserId", user.UserId);
            state.Parameters.AddWithValue("@RecoveryType", PosSaleDocumentTypes.InvoiceRecovery);
            byte[] originalHash;
            Guid? existingMovementId;
            long existingSequence = 0;
            string? existingStatus;
            await using (var reader = await state.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                    throw new FiscalOperationException("La factura no tiene una recepción fiscal verificable en este negocio.");
                existingStatus = reader.IsDBNull(7) ? null : reader.GetString(7);
                var saleStatus = reader.GetString(2);
                if (reader.GetString(1) != "DeadLettered" ||
                    (existingStatus == "Completed"
                        ? saleStatus != "Completed"
                        : saleStatus is not ("Received" or "Failed")) ||
                    reader.IsDBNull(4) ||
                    !reader.GetString(4).Contains("valorización contable positiva", StringComparison.Ordinal))
                    throw new FiscalOperationException("La factura no corresponde al fallo de valorización recuperable.");
                originalHash = reader.GetFieldValue<byte[]>(3);
                existingMovementId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
                if (!reader.IsDBNull(6)) existingSequence = reader.GetInt64(6);
            }

            if (existingMovementId is Guid movementId)
            {
                if (existingStatus == "DeadLettered")
                    throw new FiscalOperationException(
                        "La recuperación anterior también falló; revisa ese nuevo movimiento antes de continuar.");
                await transaction.CommitAsync(cancellationToken);
                if (existingStatus is "Pending" or "RetryScheduled")
                    await signals.PublishAsync(new DocumentProcessingSignal(movementId,
                        user.BusinessId, documentId, PosSaleDocumentTypes.InvoiceRecovery), cancellationToken);
                return new(documentId, movementId, existingSequence, false);
            }

            await SqlSaleRecoveryPreconditions.AssertNoEconomicEffectsAsync(
                connection, transaction, documentId, cancellationToken);
            var now = time.GetUtcNow();
            var sequence = await SqlOperationalDocumentAllocator.AllocateSequenceAsync(
                connection, transaction, user.BusinessId, now, cancellationToken);
            var newMovementId = ids.NewId();
            var payload = JsonSerializer.Serialize(new SaleDeadLetterRecoveryPayload(
                documentId, user.BusinessId, user.UserId, reason,
                Convert.ToHexString(originalHash)));
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            await using var insert = new SqlCommand("""
                INSERT dbo.DocumentProcessingJobs
                  (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
                VALUES(@MovementId,@BusinessId,@Sequence,@DocumentId,@RecoveryType,N'Pending',@Now,@Now);
                INSERT dbo.DocumentProcessingPayloads
                  (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
                VALUES(@DocumentId,@RecoveryType,@BusinessId,1,@Payload,@PayloadHash,@Now);
                """, connection, transaction);
            insert.Parameters.AddWithValue("@MovementId", newMovementId);
            insert.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            insert.Parameters.AddWithValue("@Sequence", sequence);
            insert.Parameters.AddWithValue("@DocumentId", documentId);
            insert.Parameters.AddWithValue("@RecoveryType", PosSaleDocumentTypes.InvoiceRecovery);
            insert.Parameters.AddWithValue("@Now", now);
            insert.Parameters.AddWithValue("@Payload", payload);
            insert.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 2)
                throw new DBConcurrencyException("La recuperación no pudo quedar registrada.");
            await transaction.CommitAsync(cancellationToken);
            await signals.PublishAsync(new DocumentProcessingSignal(newMovementId,
                user.BusinessId, documentId, PosSaleDocumentTypes.InvoiceRecovery), cancellationToken);
            return new(documentId, newMovementId, sequence, true);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

public sealed class SqlPosSaleRecoveryDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    SqlPosSaleDocumentHandler sales) : IConfirmedDocumentHandler
{
    public string DocumentType => PosSaleDocumentTypes.InvoiceRecovery;

    public async Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken)
    {
        var recovery = JsonSerializer.Deserialize<SaleDeadLetterRecoveryPayload>(document.Payload)
            ?? throw new InvalidOperationException("The sale recovery payload is missing.");
        if (recovery.DocumentId != document.DocumentId.Value ||
            recovery.BusinessId != document.BusinessId.Value ||
            recovery.RequestedByUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(recovery.Reason))
            throw new InvalidOperationException("The sale recovery audit envelope is invalid.");
        var session = sessions.Current;
        await SqlSaleRecoveryPreconditions.AssertNoEconomicEffectsAsync(
            session.Connection, session.Transaction, recovery.DocumentId, cancellationToken);
        await using var command = new SqlCommand("""
            SELECT payload.PayloadJson,payload.PayloadHash,payload.AcceptedAt
            FROM dbo.DocumentProcessingJobs original WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.DocumentProcessingPayloads payload WITH(UPDLOCK,HOLDLOCK)
              ON payload.DocumentId=original.DocumentId AND payload.DocumentType=original.DocumentType
            JOIN dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK)
              ON sale.DocumentId=original.DocumentId AND sale.BusinessId=original.BusinessId
            WHERE original.DocumentId=@DocumentId AND original.BusinessId=@BusinessId
              AND original.DocumentType=N'SalesInvoice' AND original.Status=N'DeadLettered'
              AND sale.ProcessingStatus IN(N'Received',N'Failed')
              AND sale.FiscalStatus IN(N'FiscalVerified',N'DianAccepted')
              AND payload.PayloadHash=sale.PayloadHash;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", recovery.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", recovery.BusinessId);
        string originalPayload;
        DateTimeOffset acceptedAt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) ||
                !string.Equals(Convert.ToHexString(reader.GetFieldValue<byte[]>(1)),
                    recovery.OriginalPayloadHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The original sale changed after its recovery was accepted.");
            originalPayload = reader.GetString(0);
            acceptedAt = reader.GetDateTimeOffset(2);
        }
        await sales.HandleAsync(new ConfirmedDocument(document.TenantId, document.BusinessId,
            document.DocumentId, PosSaleDocumentTypes.Invoice, originalPayload, acceptedAt),
            cancellationToken);
    }
}

internal static class SqlSaleRecoveryPreconditions
{
    public static async Task AssertNoEconomicEffectsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT CONVERT(bit,CASE WHEN
                EXISTS(SELECT 1 FROM dbo.SalesDocumentLines WITH(UPDLOCK,HOLDLOCK) WHERE DocumentId=@DocumentId)
                OR EXISTS(SELECT 1 FROM dbo.SalesPayments WITH(UPDLOCK,HOLDLOCK) WHERE DocumentId=@DocumentId)
                OR EXISTS(SELECT 1 FROM dbo.InventoryMovements WITH(UPDLOCK,HOLDLOCK) WHERE DocumentId=@DocumentId)
                OR EXISTS(SELECT 1 FROM dbo.Receivables WITH(UPDLOCK,HOLDLOCK) WHERE SourceDocumentId=@DocumentId)
                OR EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs WITH(UPDLOCK,HOLDLOCK) WHERE SourceDocumentId=@DocumentId)
                OR EXISTS(SELECT 1 FROM reporting.SalesReportingJobs WITH(UPDLOCK,HOLDLOCK) WHERE SourceDocumentId=@DocumentId)
                OR EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks WITH(UPDLOCK,HOLDLOCK) WHERE DocumentId=@DocumentId)
                THEN 1 ELSE 0 END);
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        if (Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
            throw new FiscalOperationException("La factura ya tiene efectos comerciales; no se puede recuperar automáticamente.");
    }
}
