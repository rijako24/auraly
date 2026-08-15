using System.Collections.Concurrent;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;

namespace Auraly.Api;

/// <summary>
/// Deterministic processing transport for the isolated Testing environment.
/// Production environments must use Service Bus or RabbitMQ.
/// </summary>
public sealed class InProcessTestingProcessingTransport(
    IServiceScopeFactory scopes,
    ILogger<InProcessTestingProcessingTransport> logger) :
    IDocumentProcessingSignalPublisher,
    IFiscalProcessingSignalPublisher,
    IAccountingProcessingSignalPublisher
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> businessGates = new();

    public async Task PublishAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        DocumentProcessingSignalCodec.Validate(signal);
        var gate = businessGates.GetOrAdd(
            signal.BusinessId,
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>();
            await worker.ProcessOneAsync(signal, cancellationToken);

            if (string.Equals(
                    signal.DocumentType,
                    Auraly.Contracts.Sales.PosSaleDocumentTypes.Invoice,
                    StringComparison.Ordinal) ||
                string.Equals(
                    signal.DocumentType,
                    Auraly.Contracts.Returns.SalesReturnDocumentTypes.SalesReturn,
                    StringComparison.Ordinal))
            {
                var fiscal = scope.ServiceProvider.GetRequiredService<FiscalProcessingCoordinator>();
                await fiscal.RequestGenerationAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    cancellationToken);
            }

            if (AccountingProcessingPolicy.Supports(signal.DocumentType))
            {
                var accounting = scope.ServiceProvider
                    .GetRequiredService<AccountingProcessingCoordinator>();
                await accounting.RequestPostingAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    signal.DocumentType,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "In-process document {DocumentId} remains available for inspection after processing failed.",
                signal.DocumentId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task PublishAsync(
        AccountingProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var processor = scope.ServiceProvider
                .GetRequiredService<SqlAccountingPostingProcessor>();
            await processor.ProcessAsync(
                signal.DocumentId,
                signal.DocumentType,
                signal.BusinessId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "In-process accounting document {DocumentId} remains available for inspection after posting failed.",
                signal.DocumentId);
        }
    }

    public async Task PublishAsync(
        FiscalProcessingSignal signal,
        DateTimeOffset? scheduledEnqueueTime = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scheduledEnqueueTime is not null || signal.Stage != FiscalProcessingStage.Generation)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<FiscalGenerationWorker>();
            await worker.ProcessAsync(
                signal.BusinessId,
                signal.DocumentId,
                "in-process-testing",
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "In-process fiscal document {DocumentId} remains available for inspection after generation failed.",
                signal.DocumentId);
        }
    }
}
