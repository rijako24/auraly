using System.Collections.Concurrent;
using Auraly.Application.DocumentProcessing;
using Auraly.Commerce.Accounting.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auraly.ServerSlice.IntegrationTests;

internal sealed class TestDocumentProcessingSignalPublisher(
    IServiceScopeFactory scopes,
    ILogger<TestDocumentProcessingSignalPublisher> logger)
    : IDocumentProcessingSignalPublisher
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> BusinessGates = new();
    private readonly ConcurrentQueue<DocumentProcessingSignal> signals = new();
    private int processingPaused;

    public async Task PublishAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        signals.Enqueue(signal);
        if (Volatile.Read(ref processingPaused) == 1) return;

        var gate = BusinessGates.GetOrAdd(
            signal.BusinessId,
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>();
            try
            {
                await worker.ProcessOneAsync(signal, cancellationToken);
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
                // A real broker has already accepted the signal. Processing failures remain
                // durable in DocumentProcessingJobs and must not turn the publishing request
                // into an HTTP failure.
                logger.LogError(
                    exception,
                    "Document {DocumentId} failed in the deterministic test transport.",
                    signal.DocumentId);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void PauseProcessing()
    {
        Drain();
        Volatile.Write(ref processingPaused, 1);
    }

    public void ResumeProcessing() => Volatile.Write(ref processingPaused, 0);

    public IReadOnlyCollection<DocumentProcessingSignal> Drain()
    {
        var drained = new List<DocumentProcessingSignal>();
        while (signals.TryDequeue(out var signal)) drained.Add(signal);
        return drained;
    }
}
