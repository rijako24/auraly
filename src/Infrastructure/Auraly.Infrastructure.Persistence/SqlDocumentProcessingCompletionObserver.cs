using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auraly.Infrastructure.Persistence;

/// <summary>
/// Publishes the derived work owned by the fiscal, accounting and reporting engines
/// from the operational server outbox.
/// </summary>
public sealed class SqlDocumentProcessingCompletionObserver(
    SqlServerConnectionFactory connections,
    IFiscalProcessingSignalPublisher fiscal,
    AccountingProcessingCoordinator accounting,
    IAccountingProcessingSignalPublisher accountingPublisher,
    ISalesReportingProcessingSignalPublisher reporting,
    SqlDocumentContinuationOutbox outbox,
    IConfiguration configuration,
    IAuralyIdGenerator ids,
    ILogger<SqlDocumentProcessingCompletionObserver> logger,
    TimeProvider timeProvider) : IDocumentProcessingCompletionObserver
{
    public async Task ObserveAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken)
    {
        try
        {
            if (configuration.GetValue("Auraly:DocumentProcessing:DurableContinuation:Enabled", false))
            {
                var pending = await outbox.LoadAsync(signal, cancellationToken)
                    ?? await outbox.SavePlanAsync(signal, await ResolveTargetsAsync(signal, cancellationToken),
                        cancellationToken);
                await PublishPendingAsync(signal, pending, cancellationToken);
                return;
            }

            foreach (var target in await ResolveTargetsAsync(signal, cancellationToken))
                await PublishAsync(signal.BusinessId, new(ids.NewId(), target), cancellationToken);
            await CompleteOperationalOutboxAsync(signal, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(signal, exception, CancellationToken.None);
            throw;
        }
    }

    private async Task<IReadOnlyList<DocumentContinuationTarget>> ResolveTargetsAsync(
        DocumentProcessingSignal signal, CancellationToken ct)
    {
        var targets = new List<DocumentContinuationTarget>();
        void Add(DocumentContinuationKind kind, Guid id, string type = "") =>
            targets.Add(new DocumentContinuationTarget(kind, id, type, targets.Count));

        if (FiscalGenerationPolicy.Supports(signal.DocumentType))
            Add(DocumentContinuationKind.FiscalGeneration, signal.DocumentId);

        if (signal.DocumentType == PurchasingDocumentTypes.GoodsReceipt)
            foreach (var id in await LoadCostSupportDocumentsAsync(signal, ct))
                Add(DocumentContinuationKind.FiscalGeneration, id);

        if (signal.EconomicEffectsEnabled && Auraly.Contracts.Sales.PosSaleDocumentTypes.IsSupported(signal.DocumentType))
            foreach (var id in await LoadChargeSupportDocumentsAsync(signal, ct))
                Add(DocumentContinuationKind.FiscalGeneration, id);

        if (signal.EconomicEffectsEnabled && AccountingProcessingPolicy.Supports(signal.DocumentType))
            foreach (var work in await accounting.ResolvePendingWorkAsync(signal.BusinessId, signal.DocumentId,
                         signal.DocumentType, ct))
                Add(DocumentContinuationKind.Accounting, work.DocumentId, work.DocumentType);

        if (signal.EconomicEffectsEnabled && SalesReportingProcessingPolicy.SupportsCommercial(signal.DocumentType))
            Add(DocumentContinuationKind.Reporting, signal.DocumentId, signal.DocumentType);

        return targets;
    }

    private async Task PublishPendingAsync(
        DocumentProcessingSignal signal, IReadOnlyList<DocumentContinuationDelivery> pending, CancellationToken ct)
    {
        var published = new List<Guid>(pending.Count);
        foreach (var delivery in pending)
        {
            try
            {
                await PublishAsync(signal.BusinessId, delivery, ct);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                try
                {
                    await outbox.AcknowledgeAsync(signal, published, delivery.MessageId, FailureMessage(error), ct);
                }
                catch (Exception acknowledgeError)
                {
                    logger.LogWarning(acknowledgeError,
                        "Could not record the interrupted continuation for movement {MovementId}.", signal.MovementId);
                }
                throw;
            }
            published.Add(delivery.MessageId);
        }
        await outbox.AcknowledgeAsync(signal, published, null, null, ct);
    }

    private Task PublishAsync(Guid businessId, DocumentContinuationDelivery delivery, CancellationToken ct)
    {
        var (kind, documentId, type, _, sourceVersion) = delivery.Target;
        return kind switch
        {
            DocumentContinuationKind.FiscalGeneration => fiscal.PublishAsync(
                new(delivery.MessageId, businessId, documentId, FiscalProcessingStage.Generation),
                cancellationToken: ct),
            DocumentContinuationKind.Accounting => accountingPublisher.PublishAsync(
                new(delivery.MessageId, businessId, documentId, type), ct),
            DocumentContinuationKind.Reporting => reporting.PublishAsync(
                new(delivery.MessageId, businessId, documentId, type, sourceVersion), ct),
            _ => throw new InvalidOperationException("El destino de publicación documental es inválido.")
        };
    }

    private async Task<IReadOnlyList<Guid>> LoadChargeSupportDocumentsAsync(
        DocumentProcessingSignal signal, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT TOP(11) e.ExpenseId FROM dbo.Expenses e
            JOIN dbo.FiscalDocumentProcesses p ON p.DocumentId=e.ExpenseId AND p.BusinessId=e.BusinessId
            WHERE e.BusinessId=@BusinessId AND e.SourceInvoiceId=@DocumentId AND p.Status=N'PendingGeneration'
            ORDER BY e.ExpenseId;
            """, connection);
        AddScope(command, signal);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetGuid(0));
        if (result.Count > InvoiceChargeApplication.MaximumChargesPerInvoice)
            throw new InvalidOperationException("The invoice has more fiscal charge documents than allowed.");
        return result;
    }

    private async Task<IReadOnlyList<Guid>> LoadCostSupportDocumentsAsync(
        DocumentProcessingSignal signal, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT TOP(101) cost.CostDocumentId
            FROM purchasing.GoodsReceiptCostDocuments cost
            JOIN dbo.GoodsReceipts receipt ON receipt.GoodsReceiptId=cost.GoodsReceiptId
              AND receipt.BusinessId=@BusinessId
            JOIN dbo.FiscalDocumentProcesses process ON process.DocumentId=cost.CostDocumentId
              AND process.BusinessId=receipt.BusinessId
            WHERE cost.GoodsReceiptId=@DocumentId
              AND cost.SourceEntryId IS NULL
              AND process.Status=N'PendingGeneration'
            ORDER BY cost.CostDocumentId;
            """, connection);
        AddScope(command, signal);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetGuid(0));
        if (result.Count > 100)
            throw new InvalidOperationException("La recepción supera el límite de 100 documentos fiscales de costo.");
        return result;
    }

    private async Task CompleteOperationalOutboxAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.ServerOutboxMessages
            SET ProcessedAt=@Now,AttemptCount=AttemptCount+1,LastError=NULL,
                RecoveryAvailableAt=NULL,RecoveryLeaseId=NULL
            WHERE DocumentId=@DocumentId
              AND DocumentType=@DocumentType
              AND ProcessedAt IS NULL
              AND Type<>N'FiscalDocument.DianAccepted'
              AND Type NOT LIKE N'doc.send.%'
              AND EXISTS
              (
                SELECT 1 FROM dbo.DocumentProcessingJobs job
                WHERE job.DocumentId=@DocumentId
                  AND job.DocumentType=@DocumentType
                  AND job.BusinessId=@BusinessId
                  AND job.Status=N'Completed'
              );
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        AddScope(command, signal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(
        DocumentProcessingSignal signal,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            const string sql = """
                UPDATE dbo.ServerOutboxMessages
                SET AttemptCount=AttemptCount+1,LastError=@Error
                WHERE DocumentId=@DocumentId
                  AND DocumentType=@DocumentType
                  AND ProcessedAt IS NULL
                  AND Type<>N'FiscalDocument.DianAccepted'
                  AND Type NOT LIKE N'doc.send.%'
                  AND EXISTS
                  (
                    SELECT 1 FROM dbo.DocumentProcessingJobs job
                    WHERE job.DocumentId=@DocumentId
                      AND job.DocumentType=@DocumentType
                      AND job.BusinessId=@BusinessId
                  );
                """;
            await using var connection = connections.Create();
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Error", FailureMessage(exception));
            AddScope(command, signal);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception recordError)
        {
            logger.LogWarning(recordError, "Could not record the publication failure for movement {MovementId}.",
                signal.MovementId);
        }
    }

    private static string FailureMessage(Exception exception)
    {
        var error = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message.Trim();
        return error.Length <= 2000 ? error : error[..2000];
    }

    private static void AddScope(
        SqlCommand command,
        DocumentProcessingSignal signal)
    {
        command.Parameters.AddWithValue("@DocumentId", signal.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", signal.DocumentType);
        command.Parameters.AddWithValue("@BusinessId", signal.BusinessId);
    }
}
