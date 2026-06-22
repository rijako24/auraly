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
                $"- pago: confirmado ({FormatAmount(payment.AmountInCents, payment.Currency)})",

            PaymentTransactionStatus.Created when IsExpired(payment) =>
                "- pago: link expirado",

            PaymentTransactionStatus.Created =>
                $"- pago: link generado ({FormatAmount(payment.AmountInCents, payment.Currency)}, expira {FormatExpiry(payment.ExpiresAt)})",

            PaymentTransactionStatus.Failed =>
                "- pago: fallido",

            PaymentTransactionStatus.Expired =>
                "- pago: link expirado",

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
            return "sin fecha de expiracion";

        var remaining = expiresAt.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return "expirado";

        if (remaining.TotalMinutes < 60)
            return $"en {(int)Math.Ceiling(remaining.TotalMinutes)} min";

        return expiresAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }
}
