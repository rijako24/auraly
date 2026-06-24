using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class OrderDeliveryExternalEscalationHandler : IExternalEscalationTargetHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderDeliveryExternalEscalationHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public bool CanHandle(string eventName, string targetType) =>
        targetType.Equals("order", StringComparison.OrdinalIgnoreCase)
        && eventName.Equals("order_created", StringComparison.OrdinalIgnoreCase);

    public Task OnAttemptSentAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) =>
        UpdateOrderAsync(attempt, DeliveryAssignmentStatus.Pending, now => order =>
        {
            order.DeliveryAssignmentRequestedAt = now;
            order.DeliveryAssignmentAcceptedAt = null;
            order.DeliveryAssignmentDeclinedAt = null;
            order.DeliveryAssignmentTimedOutAt = null;
        }, ct);

    public Task OnAttemptCompletedAsync(ExternalEscalationAttempt attempt, ExternalEscalationCompletion completion, CancellationToken ct = default)
    {
        if (!completion.OutcomeKey.Equals("accepted", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        return UpdateOrderAsync(attempt, DeliveryAssignmentStatus.Accepted, now => order =>
        {
            order.DeliveryAssignmentAcceptedAt = now;
            order.DeliveryAssignmentDeclinedAt = null;
            order.DeliveryAssignmentTimedOutAt = null;
        }, ct);
    }

    public Task OnAttemptDeclinedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) =>
        UpdateOrderAsync(attempt, DeliveryAssignmentStatus.Declined, now => order =>
        {
            order.DeliveryAssignmentDeclinedAt = now;
            order.DeliveryAssignmentTimedOutAt = null;
        }, ct);

    public Task OnAttemptTimedOutAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) =>
        UpdateOrderAsync(attempt, DeliveryAssignmentStatus.TimedOut, now => order =>
        {
            order.DeliveryAssignmentTimedOutAt = now;
        }, ct);

    public Task OnAttemptsExhaustedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) =>
        Task.CompletedTask;

    private async Task UpdateOrderAsync(
        ExternalEscalationAttempt attempt,
        DeliveryAssignmentStatus status,
        Func<DateTime, Action<Order>> applyStatusDates,
        CancellationToken ct)
    {
        var order = await _unitOfWork.Orders.GetByIdAsync(attempt.BusinessId, attempt.TargetId, ct);
        if (order is null)
            return;

        var now = DateTime.UtcNow;
        order.DeliveryAssignmentStatus = status;
        order.DeliveryExternalEscalationAttemptId = attempt.ExternalEscalationAttemptId;
        order.DeliveryAssigneeKeySnapshot = attempt.ContactKey;
        order.DeliveryAssigneeNameSnapshot = attempt.ContactNameSnapshot;
        order.DeliveryAssigneeRoleSnapshot = attempt.ContactRoleSnapshot;
        order.DeliveryAssigneePhoneSnapshot = attempt.ContactPhoneSnapshot;
        order.UpdatedAt = now;
        applyStatusDates(now)(order);

        await _unitOfWork.Orders.UpdateAsync(order, ct);
    }
}

