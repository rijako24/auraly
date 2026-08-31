using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantSubscriptionPaidCheckoutHandler(
    ITenantSubscriptionSettlementService settlements,
    ITenantSubscriptionSettlementDispatcher dispatcher,
    ILogger<TenantSubscriptionPaidCheckoutHandler> logger)
    : INonConversationalPaidCheckoutHandler
{
    public CheckoutKind Kind => CheckoutKind.TenantSubscription;

    public Task<bool> IsFulfilledAsync(
        PaymentTransaction payment,
        CancellationToken cancellationToken) =>
        settlements.IsSettledAsync(payment.PaymentTransactionId, cancellationToken);

    public async Task FulfillAsync(
        PaymentTransaction payment,
        CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentTransactionStatus.Confirmed ||
            payment.SubjectType != "TenantSubscription" ||
            payment.SubjectId is null)
            throw new InvalidOperationException(
                "El pago no corresponde a una renovación de suscripción confirmada.");

        var result = await settlements.SettleAsync(payment, cancellationToken);
        await dispatcher.DispatchAsync(result, cancellationToken);
        await settlements.MarkDispatchedAsync(
            payment.PaymentTransactionId, cancellationToken);
        logger.LogInformation(
            "Tenant subscription payment settled PaymentTransactionId={PaymentTransactionId} DocumentId={DocumentId} IsReplay={IsReplay}",
            payment.PaymentTransactionId, result.DocumentId, result.IsReplay);
    }
}
