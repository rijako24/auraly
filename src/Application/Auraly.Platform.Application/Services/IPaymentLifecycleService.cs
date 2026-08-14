using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Services;

public interface IPaymentLifecycleService
{
    Task<PaymentTransaction?> GetActiveByConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<PaymentTransaction?> GetActiveByReservationAsync(Guid reservationId, CancellationToken ct = default);
    Task<PaymentTransaction> CreatePendingCheckoutAsync(
        Guid businessId,
        Guid conversationId,
        CheckoutKind checkoutKind,
        string checkoutSnapshotJson,
        string quoteHash,
        string confirmationOutcome,
        string paymentReferenceId,
        string linkUrl,
        long amountInCents,
        string currency,
        DateTime expiresAt,
        CancellationToken ct = default);
    Task MarkConfirmedAsync(PaymentTransaction payment, string? providerTransactionId, string? webhookPayload, CancellationToken ct = default, PaymentTransactionSource? sourceOverride = null);
    Task RefreshPendingCheckoutAsync(
        PaymentTransaction payment,
        string checkoutSnapshotJson,
        string quoteHash,
        string confirmationOutcome,
        long amountInCents,
        string currency,
        CancellationToken ct = default);
    Task MarkRequiresReschedulingAsync(PaymentTransaction payment, CancellationToken ct = default);
    Task LinkReservationAsync(PaymentTransaction payment, Guid reservationId, CancellationToken ct = default);
    Task MarkSupersededAsync(PaymentTransaction payment, Guid supersededByPaymentTransactionId, CancellationToken ct = default);
    Task DiscardPendingAsync(PaymentTransaction payment, CancellationToken ct = default);

    Task<PaymentTransaction?> GetLatestByConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<PaymentTransaction?> GetPendingReschedulingByConversationAsync(Guid conversationId, CancellationToken ct = default);

    Task<bool> HasConfirmedDepositAsync(Guid conversationId, CancellationToken ct = default);

}
