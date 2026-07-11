using System.Text.Json;
using System.Security.Cryptography;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

public interface ICheckoutQuoteService
{
    string ComputeHash(CheckoutQuote quote);
}

public sealed class CheckoutQuoteService : ICheckoutQuoteService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ComputeHash(CheckoutQuote quote)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(CheckoutQuoteFingerprint.From(quote), JsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private sealed record CheckoutQuoteFingerprint(
        Guid BusinessId,
        CheckoutKind CheckoutKind,
        Guid ServiceId,
        string ServiceName,
        IReadOnlyList<CheckoutQuoteLineItem> LineItems,
        long TotalCents,
        long PayableCents,
        string Currency,
        string PaymentMethodKey,
        string TemplateId,
        string ConfirmationOutcome,
        bool RequiresManualConfirmation,
        int ManualExpirationMinutes)
    {
        public static CheckoutQuoteFingerprint From(CheckoutQuote quote) =>
            new(
                quote.BusinessId,
                quote.CheckoutKind,
                quote.ServiceId,
                quote.ServiceName,
                quote.LineItems,
                quote.TotalCents,
                quote.PayableCents,
                quote.Currency,
                quote.PaymentMethodKey,
                quote.TemplateId,
                quote.ConfirmationOutcome,
                quote.RequiresManualConfirmation,
                quote.ManualExpirationMinutes);
    }
}

public sealed record CheckoutQuote(
    Guid BusinessId,
    Guid ConversationId,
    CheckoutKind CheckoutKind,
    Guid ServiceId,
    string ServiceName,
    string? ServiceCategory,
    int DurationMinutes,
    IReadOnlyList<CheckoutQuoteLineItem> LineItems,
    long TotalCents,
    long PayableCents,
    string Currency,
    string PaymentMethodKey,
    string PaymentMethodLabel,
    int? PaymentPercentage,
    string TemplateId,
    string ConfirmationOutcome,
    IReadOnlyDictionary<string, string> RequiredFactRoles,
    IReadOnlyDictionary<string, string> SystemFactBindings,
    IReadOnlyDictionary<string, string> TemplateFactBindings,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc)
{
    public bool RequiresManualConfirmation { get; init; }
    public int ManualExpirationMinutes { get; init; } = 1440;
    public bool RequiresPaymentLink => PayableCents > 0 && !RequiresManualConfirmation;
};

public sealed record CheckoutQuoteLineItem(string Name, decimal Price, bool IncludeInCheckoutTotal = true);
