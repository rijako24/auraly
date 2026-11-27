using System.Collections.Concurrent;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

internal sealed class TestAccountingProcessingSignalPublisher(
    IServiceScopeFactory scopes) : IAccountingProcessingSignalPublisher
{
    private readonly ConcurrentQueue<AccountingProcessingSignal> pending = new();
    private int paused;
    private int lostAcknowledgement;

    public void Pause() => Volatile.Write(ref paused, 1);
    public void Resume() => Volatile.Write(ref paused, 0);
    public void LoseNextAcknowledgement() => Interlocked.Exchange(ref lostAcknowledgement, 1);

    public IReadOnlyList<AccountingProcessingSignal> Drain()
    {
        var result = new List<AccountingProcessingSignal>();
        while (pending.TryDequeue(out var signal))
            result.Add(signal);
        return result;
    }

    public async Task PublishAsync(
        AccountingProcessingSignal signal, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref paused) == 1)
        {
            pending.Enqueue(signal);
            if (Interlocked.Exchange(ref lostAcknowledgement, 0) == 1)
                throw new TimeoutException("The broker acknowledgement was lost.");
            return;
        }
        await using var scope = scopes.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        await processor.ProcessAsync(signal.DocumentId, signal.DocumentType, signal.BusinessId, cancellationToken);
    }
}
