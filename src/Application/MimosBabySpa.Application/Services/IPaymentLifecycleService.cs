using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IPaymentLifecycleService
{
    Task<PaymentTransaction?> GetActiveByConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<PaymentTransaction?> GetActiveByReservationAsync(Guid reservationId, CancellationToken ct = default);
    Task<PaymentTransaction> CreatePendingAsync(
        Guid businessId,
        Guid conversationId,
        ReservationIntentSnapshot snapshot,
        string paymentReferenceId,
        string linkUrl,
        long amountInCents,
        string currency,
        DateTime expiresAt,
        CancellationToken ct = default);
    Task MarkConfirmedAsync(PaymentTransaction payment, string? providerTransactionId, string? webhookPayload, CancellationToken ct = default);
    Task MarkRequiresReschedulingAsync(PaymentTransaction payment, CancellationToken ct = default);
    Task LinkReservationAsync(PaymentTransaction payment, Guid reservationId, CancellationToken ct = default);

    Task<PaymentTransaction?> GetLatestByConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<PaymentTransaction?> GetPendingReschedulingByConversationAsync(Guid conversationId, CancellationToken ct = default);

    Task<bool> HasConfirmedDepositAsync(Guid conversationId, CancellationToken ct = default);
}
