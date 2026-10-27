using System.Collections.Concurrent;
using Auraly.BuildingBlocks.Application.Synchronization;

namespace Auraly.ServerSlice.IntegrationTests;

internal sealed class TestPosSynchronizationPushGateway
    : IPosSynchronizationPushGateway
{
    private readonly ConcurrentQueue<PosSynchronizationInvalidation> messages = new();
    private readonly SemaphoreSlim available = new(0);
    private FailureTarget? failNext;

    public void FailNext(Guid businessId, string stream, long afterCursor) =>
        Interlocked.Exchange(ref failNext, new FailureTarget(businessId, stream, afterCursor));

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

    public Uri CreateRestaurantCommandsClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new Uri(
            $"wss://push.auraly.test/client/hubs/auraly_pos" +
            $"?tenant={tenantId:D}&business={businessId:D}&user={userId:D}" +
            "&stream=RestaurantCommands");
    }

    public Task SendAsync(
        PosSynchronizationInvalidation invalidation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Volatile.Read(ref failNext);
        if (target is not null && invalidation.BusinessId == target.BusinessId
            && invalidation.Stream == target.Stream
            && invalidation.AvailableThroughCursor > target.AfterCursor
            && Interlocked.CompareExchange(ref failNext, null, target) == target)
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

    private sealed record FailureTarget(Guid BusinessId, string Stream, long AfterCursor);
}
