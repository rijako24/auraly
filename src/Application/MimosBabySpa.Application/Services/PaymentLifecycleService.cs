using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class PaymentLifecycleService : IPaymentLifecycleService
{
    private readonly IPaymentTransactionRepository _payments;

    public PaymentLifecycleService(IPaymentTransactionRepository payments) => _payments = payments;

    public Task<PaymentTransaction?> GetActiveByConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        _payments.GetActiveByConversationIdAsync(conversationId, ct);

    public Task<PaymentTransaction?> GetActiveByReservationAsync(Guid reservationId, CancellationToken ct = default) =>
        _payments.GetActiveByReservationIdAsync(reservationId, ct);

    public async Task<PaymentTransaction> CreatePendingAsync(
        Guid businessId,
        Guid conversationId,
        ReservationIntentSnapshot snapshot,
        string paymentReferenceId,
        string linkUrl,
        long amountInCents,
        string currency,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        var tx = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            BusinessId = businessId,
            ConversationId = conversationId,
            ReservationId = null,
            PaymentReferenceId = paymentReferenceId,
            LinkUrl = linkUrl,
            AmountInCents = amountInCents,
            Currency = currency,
            Status = PaymentTransactionStatus.Created,
            Source = PaymentTransactionSource.Automated,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            Snapshot_ServiceId = snapshot.ServiceId,
            Snapshot_ReservationDateTime = snapshot.ReservationDateTime,
            Snapshot_PreferredEmployeeId = snapshot.PreferredEmployeeId,
            Snapshot_DurationMinutes = snapshot.DurationMinutes,
            Snapshot_CustomerName = snapshot.CustomerName,
            Snapshot_CustomerEmail = snapshot.CustomerEmail,
            Snapshot_CustomerPhone = snapshot.CustomerPhone,
            Snapshot_AddOnIds = snapshot.AddOnServiceIds.Count > 0
                ? string.Join(",", snapshot.AddOnServiceIds)
                : null,
            Snapshot_CustomAttributesJson = snapshot.CustomAttributesJson
        };

        await _payments.SaveAsync(tx, ct);
        return tx;
    }

    public async Task MarkConfirmedAsync(
        PaymentTransaction payment, string? providerTransactionId, string? webhookPayload, CancellationToken ct = default)
    {
        payment.ProviderTransactionId = providerTransactionId;
        payment.Status = PaymentTransactionStatus.Confirmed;
        payment.ConfirmedAt = DateTime.UtcNow;
        payment.WebhookPayloadJson = webhookPayload;
        await _payments.SaveAsync(payment, ct);
    }

    public async Task MarkRequiresReschedulingAsync(PaymentTransaction payment, CancellationToken ct = default)
    {
        payment.RequiresRescheduling = true;
        await _payments.SaveAsync(payment, ct);
    }

    public async Task LinkReservationAsync(PaymentTransaction payment, Guid reservationId, CancellationToken ct = default)
    {
        payment.ReservationId = reservationId;
        payment.RequiresRescheduling = false;
        await _payments.SaveAsync(payment, ct);
    }

    public Task<PaymentTransaction?> GetLatestByConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        _payments.GetLatestByConversationIdAsync(conversationId, ct);

    public Task<PaymentTransaction?> GetPendingReschedulingByConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        _payments.GetPendingReschedulingByConversationIdAsync(conversationId, ct);

    public async Task<bool> HasConfirmedDepositAsync(Guid conversationId, CancellationToken ct = default)
    {
        var latest = await _payments.GetLatestByConversationIdAsync(conversationId, ct);
        return latest?.Status == PaymentTransactionStatus.Confirmed;
    }
}
