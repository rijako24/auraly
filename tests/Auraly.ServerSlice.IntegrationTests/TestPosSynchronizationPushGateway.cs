using System.Collections.Concurrent;
using Auraly.BuildingBlocks.Application.Synchronization;

namespace Auraly.ServerSlice.IntegrationTests;

internal sealed class TestPosSynchronizationPushGateway
    : IPosSynchronizationPushGateway
{
    private readonly ConcurrentQueue<PosSynchronizationInvalidation> messages = new();
    private readonly SemaphoreSlim available = new(0);
    private int failNext;

    public void FailNext() => Interlocked.Exchange(ref failNext, 1);

    public Uri CreateClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new Uri(
            $"wss://push.auraly.test/client/hubs/auraly_pos" +
            $"?tenant={tenantId:D}&business={businessId:D}&device={deviceId:D}");
    }

    public Uri CreateUserClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new Uri(
            $"wss://push.auraly.test/client/hubs/auraly_pos" +
            $"?tenant={tenantId:D}&business={businessId:D}&user={userId:D}");
    }

    public Task SendAsync(
        PosSynchronizationInvalidation invalidation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref failNext, 0) == 1)
            throw new HttpRequestException("Simulated transient push failure.");

        messages.Enqueue(invalidation);
        available.Release();
        return Task.CompletedTask;
    }

    public async Task<PosSynchronizationInvalidation> ReadAsync(
        CancellationToken cancellationToken)
    {
        await available.WaitAsync(cancellationToken);
        return messages.TryDequeue(out var value)
            ? value
            : throw new InvalidOperationException(
                "The synchronization signal was consumed without a message.");
    }

    public IReadOnlyCollection<PosSynchronizationInvalidation> Drain()
    {
        var values = new List<PosSynchronizationInvalidation>();
        while (messages.TryDequeue(out var value))
        {
            available.Wait(0);
            values.Add(value);
        }
        return values;
    }
}
