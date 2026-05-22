using System.Globalization;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents;

internal static class TurnContextPaymentFormatter
{
    internal static string FormatDepositLine(BookingPolicyParams? bookingPolicy)
    {
        if (bookingPolicy?.DepositRequired == true && bookingPolicy.DepositPercentage > 0)
        {
            return $"- anticipo: requerido ({bookingPolicy.DepositPercentage}% — se calcula tras resolve_pricing)";
        }

        return "- anticipo: no requerido (puedes cerrar con create_reservation tras confirmación verbal)";
    }

    internal static string? FormatPaymentLine(PaymentTransaction? payment, BookingPolicyParams? bookingPolicy)
    {
        if (bookingPolicy?.DepositRequired != true)
            return null;

        if (payment is null)
            return "- pago: no iniciado";

        return payment.Status switch
        {
            PaymentTransactionStatus.Confirmed =>
                $"- pago: confirmado ({FormatAmount(payment.AmountInCents, payment.Currency)})",

            PaymentTransactionStatus.Created when IsExpired(payment) =>
                "- pago: link expirado — regenerar con generate_payment_link",

            PaymentTransactionStatus.Created =>
                $"- pago: link generado ({FormatAmount(payment.AmountInCents, payment.Currency)}, expira {FormatExpiry(payment.ExpiresAt)})",

            PaymentTransactionStatus.Failed =>
                "- pago: fallido — regenerar con generate_payment_link",

            PaymentTransactionStatus.Expired =>
                "- pago: link expirado — regenerar con generate_payment_link",

            _ => "- pago: no iniciado"
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
            return "sin fecha de expiración";

        var remaining = expiresAt.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return "expirado";

        if (remaining.TotalMinutes < 60)
            return $"en {(int)Math.Ceiling(remaining.TotalMinutes)} min";

        return expiresAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }
}
