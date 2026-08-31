using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Services;

public sealed record CheckoutPaymentContext(
    Guid BusinessId,
    Guid ConversationId,
    PaymentTransaction? ActivePayment = null);

public interface ICheckoutPaymentCoordinator
{
    Task<CheckoutPaymentDiscardResult> DiscardActiveCheckoutAsync(
        CheckoutPaymentContext context,
        CheckoutKind checkoutKind,
        CancellationToken ct = default);

    Task<CheckoutPaymentLinkResult> EnsurePaymentLinkAsync(
        CheckoutPaymentContext context,
        CheckoutQuote quote,
        string paymentPhone,
        string checkoutSnapshotJson,
        CancellationToken ct = default);

    Task<CheckoutPaymentLinkResult> EnsureManualPaymentAsync(
        CheckoutPaymentContext context,
        CheckoutQuote quote,
        string checkoutSnapshotJson,
        CancellationToken ct = default);

    Task<CheckoutPaymentDiscardResult> DiscardActiveCheckoutAsync(
        AgentConversationContext ctx,
        CheckoutKind checkoutKind,
        CancellationToken ct = default);

    Task<CheckoutPaymentLinkResult> EnsurePaymentLinkAsync(
        AgentConversationContext ctx,
        CheckoutQuote quote,
        string paymentPhone,
        string checkoutSnapshotJson,
        CancellationToken ct = default);

    Task<CheckoutPaymentLinkResult> EnsureManualPaymentAsync(
        AgentConversationContext ctx,
        CheckoutQuote quote,
        string checkoutSnapshotJson,
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
        AgentConversationContext ctx,
        CheckoutKind checkoutKind,
        CancellationToken ct = default)
    {
        var result = await DiscardActiveCheckoutAsync(
            new CheckoutPaymentContext(ctx.BusinessId, ctx.ConversationId, ctx.ActivePayment),
            checkoutKind,
            ct);
        if (result.DiscardedPayment is not null)
            ctx.ActivePayment = null;
        return result;
    }

    public async Task<CheckoutPaymentDiscardResult> DiscardActiveCheckoutAsync(
        CheckoutPaymentContext context,
        CheckoutKind checkoutKind,
        CancellationToken ct = default)
    {
        var activePayment = context.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(context.ConversationId, ct);

        if (activePayment is null
            || activePayment.Status != PaymentTransactionStatus.Created
            || activePayment.CheckoutKind != checkoutKind)
        {
            return CheckoutPaymentDiscardResult.None;
        }

        await _paymentLifecycle.DiscardPendingAsync(activePayment, ct);
        return new CheckoutPaymentDiscardResult(activePayment);
    }

    public async Task<CheckoutPaymentLinkResult> EnsurePaymentLinkAsync(
        AgentConversationContext ctx,
        CheckoutQuote quote,
        string paymentPhone,
        string checkoutSnapshotJson,
        CancellationToken ct = default)
    {
        var result = await EnsurePaymentLinkAsync(
            new CheckoutPaymentContext(ctx.BusinessId, ctx.ConversationId, ctx.ActivePayment),
            quote,
            paymentPhone,
            checkoutSnapshotJson,
            ct);
        if (result.Payment is not null)
            ctx.ActivePayment = result.Payment;
        return result;
    }

    public async Task<CheckoutPaymentLinkResult> EnsurePaymentLinkAsync(
        CheckoutPaymentContext context,
        CheckoutQuote quote,
        string paymentPhone,
        string checkoutSnapshotJson,
        CancellationToken ct = default)
    {
        var activePayment = context.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(context.ConversationId, ct);
        var quoteHash = _quotes.ComputeHash(quote);

        if (!string.IsNullOrWhiteSpace(activePayment?.LinkUrl)
            && IsReusableActiveCheckout(activePayment, quote, quoteHash))
        {
            await _paymentLifecycle.RefreshPendingCheckoutAsync(
                activePayment,
                checkoutSnapshotJson,
                quoteHash,
                quote.ConfirmationOutcome,
                quote.PayableCents,
                quote.Currency,
                ct);
            return CheckoutPaymentLinkResult.Ok(activePayment.LinkUrl!, activePayment);
        }

        var supersededPayment = activePayment is { Status: PaymentTransactionStatus.Created }
            ? activePayment
            : null;
        var result = await _paymentLinks.GenerateAnticipoLinkAsync(
            new PaymentLinkRequest(
                context.BusinessId,
                context.ConversationId,
                paymentPhone,
                quote.ServiceName,
                quote.PayableCents,
                quote.Currency,
                ExpirationMinutes: 60),
            ct);

        if (!result.Success)
            return CheckoutPaymentLinkResult.Fail(result.ErrorMessage ?? "Failed to generate payment link.");

        var payment = await _paymentLifecycle.CreatePendingCheckoutAsync(
            context.BusinessId,
            context.ConversationId,
            quote.CheckoutKind,
            checkoutSnapshotJson,
            quoteHash,
            quote.ConfirmationOutcome,
            result.PaymentReferenceId!,
            result.PaymentLinkUrl!,
            quote.PayableCents,
            quote.Currency,
            result.ExpiresAt ?? DateTime.UtcNow.AddHours(1),
            ct,
            result.MerchantConfigurationVersion);

        if (supersededPayment is not null)
            await _paymentLifecycle.MarkSupersededAsync(supersededPayment, payment.PaymentTransactionId, ct);

        return CheckoutPaymentLinkResult.Ok(payment.LinkUrl!, payment);
    }

    public async Task<CheckoutPaymentLinkResult> EnsureManualPaymentAsync(
        AgentConversationContext ctx,
        CheckoutQuote quote,
        string checkoutSnapshotJson,
        CancellationToken ct = default)
    {
        var result = await EnsureManualPaymentAsync(
            new CheckoutPaymentContext(ctx.BusinessId, ctx.ConversationId, ctx.ActivePayment),
            quote,
            checkoutSnapshotJson,
            ct);
        if (result.Payment is not null)
            ctx.ActivePayment = result.Payment;
        return result;
    }

    public async Task<CheckoutPaymentLinkResult> EnsureManualPaymentAsync(
        CheckoutPaymentContext context,
        CheckoutQuote quote,
        string checkoutSnapshotJson,
        CancellationToken ct = default)
    {
        var activePayment = context.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(context.ConversationId, ct);
        var quoteHash = _quotes.ComputeHash(quote);

        if (activePayment is not null
            && string.IsNullOrWhiteSpace(activePayment.LinkUrl)
            && IsReusableActiveCheckout(activePayment, quote, quoteHash))
        {
            await _paymentLifecycle.RefreshPendingCheckoutAsync(
                activePayment,
                checkoutSnapshotJson,
                quoteHash,
                quote.ConfirmationOutcome,
                quote.PayableCents,
                quote.Currency,
                ct);
            return CheckoutPaymentLinkResult.OkManual(activePayment);
        }

        var supersededPayment = activePayment is { Status: PaymentTransactionStatus.Created }
            ? activePayment
            : null;
        var issuedAt = DateTime.UtcNow;
        var payment = await _paymentLifecycle.CreatePendingCheckoutAsync(
            context.BusinessId,
            context.ConversationId,
            quote.CheckoutKind,
            checkoutSnapshotJson,
            quoteHash,
            quote.ConfirmationOutcome,
            $"manual-{quote.CheckoutKind.ToString().ToLowerInvariant()}-{context.ConversationId:N}-{issuedAt:yyyyMMddHHmmss}",
            string.Empty,
            quote.PayableCents,
            quote.Currency,
            issuedAt.AddMinutes(Math.Max(1, quote.ManualExpirationMinutes)),
            ct);

        if (supersededPayment is not null)
            await _paymentLifecycle.MarkSupersededAsync(supersededPayment, payment.PaymentTransactionId, ct);

        return CheckoutPaymentLinkResult.OkManual(payment);
    }

    private static bool IsReusableActiveCheckout(PaymentTransaction? payment, CheckoutQuote quote, string quoteHash) =>
        payment is not null
        && payment.Status == PaymentTransactionStatus.Created
        && payment.ExpiresAt.HasValue
        && payment.ExpiresAt.Value > DateTime.UtcNow
        && payment.CheckoutKind == quote.CheckoutKind
        && string.Equals(payment.QuoteHash, quoteHash, StringComparison.Ordinal)
        && payment.AmountInCents == quote.PayableCents
        && string.Equals(payment.Currency, quote.Currency, StringComparison.OrdinalIgnoreCase)
        && string.Equals(payment.ConfirmationOutcome, quote.ConfirmationOutcome, StringComparison.OrdinalIgnoreCase);
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

    public static CheckoutPaymentLinkResult OkManual(PaymentTransaction payment) =>
        new(true, null, payment, null);

    public static CheckoutPaymentLinkResult Fail(string errorMessage) =>
        new(false, null, null, errorMessage);
}
