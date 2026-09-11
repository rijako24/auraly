using System.Collections.Concurrent;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class TestPosApprovalCreatedNotifier : IPosApprovalCreatedNotifier
{
    private readonly ConcurrentQueue<PosApprovalRequestView> notifications = new();

    public Task NotifyAsync(
        PosApprovalRequestView request,
        CancellationToken cancellationToken)
    {
        notifications.Enqueue(request);
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<PosApprovalRequestView> Drain()
    {
        var drained = new List<PosApprovalRequestView>();
        while (notifications.TryDequeue(out var notification))
            drained.Add(notification);
        return drained;
    }
}
