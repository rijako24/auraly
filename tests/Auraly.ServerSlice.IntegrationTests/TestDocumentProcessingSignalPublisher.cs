using System.Collections.Concurrent;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

internal sealed class TestDocumentProcessingSignalPublisher(
    IServiceScopeFactory scopes)
    : IDocumentProcessingSignalPublisher
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> BusinessGates = new();

    public async Task PublishAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            }
            catch (DocumentProcessingMessageException)
            {
                // Publishing succeeded. A real broker leaves this delivery unacknowledged
                // while the preceding movement blocks the business session.
        }
        }
        finally
        {
            gate.Release();
        }
    }
}
