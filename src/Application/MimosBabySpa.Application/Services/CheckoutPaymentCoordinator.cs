using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

public interface ICheckoutPaymentCoordinator
{
    Task<CheckoutPaymentDiscardResult> DiscardActiveCheckoutAsync(
        AgentToolContext ctx,
        CheckoutKind checkoutKind,
        CancellationToken ct = default);

    Task<CheckoutPaymentLinkResult> EnsurePaymentLinkAsync(
        AgentToolContext ctx,
        CheckoutQuote quote,
        string paymentPhone,
        string checkoutSnapshotJson,
        ReservationIntentSnapshot? reservationSnapshot = null,
        CancellationToken ct = default);
}

public sealed class CheckoutPaymentCoordinator : ICheckoutPaymentCoordinator
{
    private readonly IPaymentLinkService _paymentLinks;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly ICheckoutQuoteService _quotes;

    public CheckoutPaymentCoordinator(
        IPaymentLinkService paymentLinks,
        IPaymentLifecycleService paymentLifecycle,
        ICheckoutQuoteService quotes)
    {
        _paymentLinks = paymentLinks;
        _paymentLifecycle = paymentLifecycle;
        _quotes = quotes;
    }

    public async Task<CheckoutPaymentDiscardResult> DiscardActiveCheckoutAsync(
        AgentToolContext ctx,
        CheckoutKind checkoutKind,
        CancellationToken ct = default)
    {
        var activePayment = ctx.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(ctx.ConversationId, ct);

        if (activePayment is null
            || activePayment.Status != PaymentTransactionStatus.Created
            || activePayment.CheckoutKind != checkoutKind)
        {
            return CheckoutPaymentDiscardResult.None;
        }

        await _paymentLifecycle.DiscardPendingAsync(activePayment, ct);
        ctx.ActivePayment = null;
        return new CheckoutPaymentDiscardResult(activePayment);
    }

    public async Task<CheckoutPaymentLinkResult> EnsurePaymentLinkAsync(
        AgentToolContext ctx,
        CheckoutQuote quote,
        string paymentPhone,
        string checkoutSnapshotJson,
        ReservationIntentSnapshot? reservationSnapshot = null,
        CancellationToken ct = default)
    {
        var activePayment = ctx.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(ctx.ConversationId, ct);
        var quoteHash = _quotes.ComputeHash(quote);

        if (activePayment?.LinkUrl is not null
            && activePayment.ExpiresAt.HasValue
            && activePayment.ExpiresAt.Value > DateTime.UtcNow
            && activePayment.CheckoutKind == quote.CheckoutKind
            && string.Equals(activePayment.QuoteHash, quoteHash, StringComparison.Ordinal))
        {
            ctx.ActivePayment = activePayment;
            return CheckoutPaymentLinkResult.Ok(activePayment.LinkUrl, activePayment);
        }

        PaymentTransaction? supersededPayment = null;
        if (activePayment is not null && activePayment.Status == PaymentTransactionStatus.Created)
            supersededPayment = activePayment;

        var result = await _paymentLinks.GenerateAnticipoLinkAsync(
            new PaymentLinkRequest(
                ctx.BusinessId,
                ctx.ConversationId,
                paymentPhone,
                quote.ServiceName,
                quote.PayableCents,
                quote.Currency,
                ExpirationMinutes: 60),
            ct);

        if (!result.Success)
            return CheckoutPaymentLinkResult.Fail(result.ErrorMessage ?? "Failed to generate payment link.");

        var payment = await _paymentLifecycle.CreatePendingCheckoutAsync(
            ctx.BusinessId,
            ctx.ConversationId,
            quote.CheckoutKind,
            checkoutSnapshotJson,
            quoteHash,
            quote.ConfirmationOutcome,
            result.PaymentReferenceId!,
            result.PaymentLinkUrl!,
            quote.PayableCents,
            quote.Currency,
            result.ExpiresAt ?? DateTime.UtcNow.AddHours(1),
            reservationSnapshot,
            ct);

        if (supersededPayment is not null)
            await _paymentLifecycle.MarkSupersededAsync(supersededPayment, payment.PaymentTransactionId, ct);

        ctx.ActivePayment = payment;
        return CheckoutPaymentLinkResult.Ok(payment.LinkUrl!, payment);
    }
}

public sealed record CheckoutPaymentDiscardResult(PaymentTransaction? DiscardedPayment)
{
    public static CheckoutPaymentDiscardResult None { get; } = new((PaymentTransaction?)null);
}

public sealed record CheckoutPaymentLinkResult(
    bool Success,
    string? LinkUrl,
    PaymentTransaction? Payment,
    string? ErrorMessage)
{
    public static CheckoutPaymentLinkResult Ok(string linkUrl, PaymentTransaction payment) =>
        new(true, linkUrl, payment, null);

    public static CheckoutPaymentLinkResult Fail(string errorMessage) =>
        new(false, null, null, errorMessage);
}
