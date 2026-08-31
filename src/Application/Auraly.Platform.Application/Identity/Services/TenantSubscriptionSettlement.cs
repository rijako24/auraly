using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Application.Identity.Services;

public sealed record TenantSubscriptionSettlementResult(
    Guid DocumentId,
    Guid BusinessId,
    bool IsReplay);

public interface ITenantSubscriptionSettlementService
{
    Task<bool> IsSettledAsync(
        Guid paymentTransactionId,
        CancellationToken cancellationToken);

    Task<TenantSubscriptionSettlementResult> SettleAsync(
        PaymentTransaction payment,
        CancellationToken cancellationToken);

    Task MarkDispatchedAsync(
        Guid paymentTransactionId,
        CancellationToken cancellationToken);
}

public interface ITenantSubscriptionSettlementDispatcher
{
    Task DispatchAsync(
        TenantSubscriptionSettlementResult settlement,
        CancellationToken cancellationToken);
}
