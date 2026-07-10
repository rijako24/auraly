using System.Globalization;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents;

internal static class TurnContextPaymentFormatter
{
    internal static string? FormatPaymentLine(PaymentTransaction? payment)
    {
        if (payment is null)
            return null;

        return payment.Status switch
        {
            PaymentTransactionStatus.Confirmed =>
                $"- payment: status=confirmed; amount={FormatAmount(payment.AmountInCents, payment.Currency)}",

            PaymentTransactionStatus.Created when IsExpired(payment) =>
                "- payment: status=expired",

            PaymentTransactionStatus.Created =>
                $"- payment: status=created; amount={FormatAmount(payment.AmountInCents, payment.Currency)}; expires={FormatExpiry(payment.ExpiresAt)}",

            PaymentTransactionStatus.Failed =>
                "- payment: status=failed",

            PaymentTransactionStatus.Expired =>
                "- payment: status=expired",

            _ => null
        };
    }

    private static bool IsExpired(PaymentTransaction payment) =>
        payment.ExpiresAt.HasValue && payment.ExpiresAt.Value <= DateTime.UtcNow;

    private static string FormatAmount(long amountInCents, string currency)
    {
        var amount = amountInCents / 100m;
        return $"{currency} ${amount.ToString("N0", CultureInfo.InvariantCulture)}";
    }

    private static string FormatExpiry(DateTime? expiresAt)
    {
        if (!expiresAt.HasValue)
            return "missing";

        var remaining = expiresAt.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return "expired";

        if (remaining.TotalMinutes < 60)
            return $"in_{(int)Math.Ceiling(remaining.TotalMinutes)}_min";

        return expiresAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }
}
