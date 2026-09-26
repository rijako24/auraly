using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleServerStore
{
    public async Task<StoredAcceptedBlockedSale?> LoadAcceptedBlockedSaleAsync(
        Guid businessId, Guid documentId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.IdempotencyKey,s.SnapshotJson
            FROM dbo.SalesDocuments d
            JOIN dbo.FiscalSnapshots s ON s.DocumentId=d.DocumentId
            JOIN dbo.FiscalDocumentProcesses p
              ON p.DocumentId=d.DocumentId AND p.BusinessId=d.BusinessId
            WHERE d.BusinessId=@BusinessId AND d.DocumentId=@DocumentId
              AND d.DocumentType=N'SalesInvoice' AND d.SourceMode=N'Online'
              AND d.ProcessingStatus IN(N'Blocked',N'Received',N'Completed')
              AND d.FiscalStatus=N'DianAccepted'
              AND p.Status=N'DianAccepted'
              AND s.IntegrityStatus=N'FiscalVerified';
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredAcceptedBlockedSale(reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async Task<(StoredPosSale Sale, bool Enqueued)> RecoverAcceptedBlockedSaleAsync(
        StorePosSaleReceptionCommand recovery, CancellationToken cancellationToken)
    {
        var request = recovery.Request;
        if (!recovery.Verification.IsVerified || request.FiscalSnapshot is null ||
            request.SourceMode != SaleSourceModes.Online ||
            request.CommercialSnapshot.DocumentType != PosSaleDocumentTypes.Invoice)
            throw new ArgumentException(
                "Solo se puede recuperar una factura electrónica en línea verificada.",
                nameof(recovery));

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await EnsureAndLockBusinessCursorAsync(
                connection, transaction, request.BusinessId, recovery.ReceivedAt,
                cancellationToken);

            const string stateSql = """
                SELECT d.ProcessingStatus,d.FiscalStatus,d.IdempotencyKey,
                       d.PayloadHash,s.PayloadHash,
                       CONVERT(bit,CASE WHEN EXISTS(
                         SELECT 1 FROM dbo.DocumentProcessingJobs j
                         WHERE j.DocumentId=d.DocumentId)
                         OR EXISTS(SELECT 1 FROM dbo.DocumentProcessingPayloads payload
                           WHERE payload.DocumentId=d.DocumentId)
                         OR EXISTS(SELECT 1 FROM dbo.SalesDocumentLines line
                           WHERE line.DocumentId=d.DocumentId)
                         OR EXISTS(SELECT 1 FROM dbo.SalesPayments payment
                           WHERE payment.DocumentId=d.DocumentId)
                         OR EXISTS(SELECT 1 FROM dbo.Receivables receivable
                           WHERE receivable.SourceDocumentId=d.DocumentId)
                         OR EXISTS(SELECT 1 FROM dbo.InventoryMovements movement
                           WHERE movement.DocumentId=d.DocumentId)
                         OR EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link
                           WHERE link.DocumentId=d.DocumentId)
                         OR (@SourceOrderId IS NOT NULL AND EXISTS(
                           SELECT 1 FROM dbo.OrderInvoiceLinks link
                           WHERE link.OrderId=@SourceOrderId))
                       THEN 1 ELSE 0 END) HasEconomicEffects,
                       CONVERT(bit,CASE WHEN EXISTS(
                         SELECT 1 FROM dbo.DocumentProcessingJobs j
                         WHERE j.DocumentId=d.DocumentId AND j.DocumentType=d.DocumentType)
                       THEN 1 ELSE 0 END) HasWork,
                       CONVERT(bit,CASE WHEN @SourceOrderId IS NOT NULL AND EXISTS(
                         SELECT 1 FROM dbo.OrderInvoiceLinks link
                         WHERE link.OrderId=@SourceOrderId
                           AND (link.BusinessId<>d.BusinessId OR link.DocumentId<>d.DocumentId))
                       THEN 1 ELSE 0 END) OtherOrderLinked
                FROM dbo.SalesDocuments d WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.Businesses business ON business.BusinessId=d.BusinessId
                  AND business.TenantId=@TenantId
                JOIN dbo.FiscalDocuments fiscal WITH(UPDLOCK,HOLDLOCK)
                  ON fiscal.DocumentId=d.DocumentId AND fiscal.BusinessId=d.BusinessId
                JOIN dbo.FiscalSnapshots s WITH(UPDLOCK,HOLDLOCK)
                  ON s.DocumentId=d.DocumentId
                JOIN dbo.FiscalDocumentProcesses p WITH(UPDLOCK,HOLDLOCK)
                  ON p.DocumentId=d.DocumentId AND p.BusinessId=d.BusinessId
                WHERE d.DocumentId=@DocumentId AND d.BusinessId=@BusinessId
                  AND d.DocumentType=N'SalesInvoice' AND d.SourceMode=N'Online'
                  AND fiscal.SourceDocumentType=N'SalesInvoice'
                  AND fiscal.FiscalDocumentType=N'Invoice'
                  AND fiscal.FiscalStatus=N'DianAccepted'
                  AND fiscal.UniqueCode=d.CufeReceived
                  AND p.Status=N'DianAccepted' AND p.TrackId IS NOT NULL
                  AND s.IntegrityStatus=N'FiscalVerified'
                  AND s.CufeReceived=d.CufeReceived
                  AND EXISTS(SELECT 1 FROM dbo.FiscalTransmissionAttempts attempt
                    WHERE attempt.DocumentId=d.DocumentId
                      AND attempt.Disposition=N'Accepted'
                      AND attempt.StatusCode=N'00'
                      AND attempt.MayHaveReachedDian=1);
                """;
            string status;
            bool alreadyEnqueued;
            await using (var state = new SqlCommand(stateSql, connection, transaction))
            {
                state.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                state.Parameters.AddWithValue("@TenantId", request.TenantId);
                state.Parameters.AddWithValue("@DocumentId", request.DocumentId);
                state.Parameters.Add("@SourceOrderId", SqlDbType.UniqueIdentifier).Value =
                    (object?)request.SourceOrderId ?? DBNull.Value;
                await using var reader = await state.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "La factura no tiene aceptación DIAN comprobada en esta empresa.");
                status = reader.GetString(0);
                if (reader.GetString(1) != FiscalDocumentStatusCodes.DianAccepted ||
                    reader.GetString(2) != recovery.IdempotencyKey ||
                    !reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(recovery.PayloadHash) ||
                    !reader.GetFieldValue<byte[]>(4).AsSpan().SequenceEqual(recovery.PayloadHash))
                    throw new InvalidOperationException(
                        "La factura aceptada cambió de estado o de contenido.");
                alreadyEnqueued = status is "Received" or "Completed";
                if (reader.GetBoolean(7) ||
                    alreadyEnqueued != reader.GetBoolean(5) ||
                    alreadyEnqueued != reader.GetBoolean(6))
                    throw new InvalidOperationException(
                        "La factura aceptada tiene un estado comercial incompatible con sus efectos.");
                if (status is not ("Blocked" or "Received" or "Completed"))
                    throw new InvalidOperationException(
                        "La factura aceptada no se puede recuperar en su estado actual.");
            }

            if (alreadyEnqueued)
            {
                var current = await FindInternalAsync(connection, transaction,
                    request.BusinessId, request.DocumentId, recovery.IdempotencyKey,
                    cancellationToken)
                    ?? throw new InvalidOperationException("No se encontró la factura recuperada.");
                await transaction.CommitAsync(cancellationToken);
                return (current, false);
            }

            if (request.SourceOrderId is Guid sourceOrderId)
            {
                await using var reserveOrder = new SqlCommand("""
                    INSERT dbo.OrderInvoiceLinks(
                      OrderInvoiceLinkId,BusinessId,OrderId,DocumentId,OperationId,CreatedAt)
                    SELECT @LinkId,@BusinessId,@OrderId,@DocumentId,receipt.OperationId,@CreatedAt
                    FROM dbo.Orders source WITH(UPDLOCK,HOLDLOCK)
                    LEFT JOIN dbo.OnlineSalesCheckoutReceipts receipt
                      ON receipt.DocumentId=@DocumentId
                     AND receipt.BusinessId=@BusinessId
                     AND receipt.SourceOrderId=@OrderId
                    WHERE source.OrderId=@OrderId AND source.BusinessId=@BusinessId
                      AND source.CustomerConfirmed=1 AND source.Status IN(2,4)
                      AND source.OrdersWarehouseId IS NOT NULL;
                    IF @@ROWCOUNT<>1
                        THROW 51022,N'El pedido de origen no está disponible para recuperar la factura.',1;
                    """, connection, transaction);
                reserveOrder.Parameters.AddWithValue("@LinkId", idGenerator.NewId());
                reserveOrder.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                reserveOrder.Parameters.AddWithValue("@OrderId", sourceOrderId);
                reserveOrder.Parameters.AddWithValue("@DocumentId", request.DocumentId);
                reserveOrder.Parameters.AddWithValue("@CreatedAt", recovery.ReceivedAt);
                await reserveOrder.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var update = new SqlCommand("""
                UPDATE dbo.SalesDocuments
                SET ProcessingStatus=N'Received',
                    CufeCalculated=@CufeCalculated
                WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
                  AND ProcessingStatus=N'Blocked' AND FiscalStatus=N'DianAccepted';
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                update.Parameters.AddWithValue("@DocumentId", request.DocumentId);
                update.Parameters.AddWithValue("@CufeCalculated",
                    recovery.Verification.CufeCalculated ?? request.FiscalSnapshot.Cufe);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "La factura aceptada cambió durante su recuperación.");
            }

            await EnqueueDocumentAsync(connection, transaction, recovery, cancellationToken);
            var stored = await FindInternalAsync(connection, transaction,
                request.BusinessId, request.DocumentId, recovery.IdempotencyKey,
                cancellationToken)
                ?? throw new InvalidOperationException("No se encontró la factura recuperada.");
            await transaction.CommitAsync(cancellationToken);
            return (stored, true);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

    }
}
