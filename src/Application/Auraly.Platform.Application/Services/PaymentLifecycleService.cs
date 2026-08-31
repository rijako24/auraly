using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public sealed class PaymentLifecycleService : IPaymentLifecycleService
{
    private readonly IPaymentTransactionRepository _payments;

    public PaymentLifecycleService(IPaymentTransactionRepository payments) => _payments = payments;

    public Task<PaymentTransaction?> GetActiveByConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        _payments.GetActiveByConversationIdAsync(conversationId, ct);

    public Task<PaymentTransaction?> GetActiveByReservationAsync(Guid reservationId, CancellationToken ct = default) =>
        _payments.GetActiveByReservationIdAsync(reservationId, ct);

    public async Task<PaymentTransaction> CreatePendingCheckoutAsync(
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
        CancellationToken ct = default,
        int merchantConfigurationVersion = 1)
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
            CheckoutKind = checkoutKind,
            CheckoutSnapshotJson = checkoutSnapshotJson,
            MerchantConfigurationVersion = merchantConfigurationVersion,
            QuoteHash = quoteHash,
            ConfirmationOutcome = confirmationOutcome
        };

        await _payments.SaveAsync(tx, ct);
        return tx;
    }

    public async Task RefreshPendingCheckoutAsync(
        PaymentTransaction payment,
        string checkoutSnapshotJson,
        string quoteHash,
        string confirmationOutcome,
        long amountInCents,
        string currency,
        CancellationToken ct = default)
    {
        if (payment.Status != PaymentTransactionStatus.Created)
            return;

        payment.CheckoutSnapshotJson = checkoutSnapshotJson;
        payment.QuoteHash = quoteHash;
        payment.ConfirmationOutcome = confirmationOutcome;
        payment.AmountInCents = amountInCents;
        payment.Currency = currency;
        await _payments.SaveAsync(payment, ct);
    }

    public async Task MarkConfirmedAsync(
        PaymentTransaction payment, string? providerTransactionId, string? webhookPayload, CancellationToken ct = default, PaymentTransactionSource? sourceOverride = null)
    {
        payment.ProviderTransactionId = providerTransactionId;
        payment.Status = PaymentTransactionStatus.Confirmed;
        payment.ConfirmedAt = DateTime.UtcNow;
        payment.WebhookPayloadJson = webhookPayload;
        if (sourceOverride.HasValue)
            payment.Source = sourceOverride.Value;
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

    public async Task MarkSupersededAsync(
        PaymentTransaction payment, Guid supersededByPaymentTransactionId, CancellationToken ct = default)
    {
        payment.Status = PaymentTransactionStatus.Superseded;
        payment.SupersededAt = DateTime.UtcNow;
        payment.SupersededByPaymentTransactionId = supersededByPaymentTransactionId;
        await _payments.SaveAsync(payment, ct);
    }

    public async Task DiscardPendingAsync(PaymentTransaction payment, CancellationToken ct = default)
    {
        if (payment.Status != PaymentTransactionStatus.Created)
            return;

        if (string.IsNullOrWhiteSpace(payment.LinkUrl))
        {
            await _payments.DeleteAsync(payment, ct);
            return;
        }

        payment.Status = PaymentTransactionStatus.Abandoned;
        payment.SupersededAt = DateTime.UtcNow;
        payment.SupersededByPaymentTransactionId = null;
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
