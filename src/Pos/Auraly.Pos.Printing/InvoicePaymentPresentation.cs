using System.Globalization;
using System.Net;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;

namespace Auraly.Pos.Printing;

public static class InvoicePaymentPresentation
{
    public static IReadOnlyList<(string Label, string Value)> Fields(SalesInvoicePrintDetails details, DateTimeOffset issuedAt)
    {
        if (details.PaymentFormCode == "1")
            return [("Forma de pago", "Contado")];
        if (details.PaymentFormCode != "2") throw new InvalidOperationException("Unsupported fiscal payment form.");
        var days = details.PaymentDueDate.DayNumber - DateOnly.FromDateTime(DianFiscalDateTime.InColombia(issuedAt).Date).DayNumber;
        if (days < 0) throw new InvalidOperationException("The invoice due date precedes its issue date.");
        return [("Forma de pago", "Crédito"), ("Plazo", $"{days} días"),
            ("Vencimiento", details.PaymentDueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))];
    }

    public static string Html(SalesInvoicePrintDetails details, DateTimeOffset issuedAt) =>
        string.Join("", Fields(details, issuedAt).Select(field =>
            $"<div><strong>{WebUtility.HtmlEncode(field.Label)}:</strong> {WebUtility.HtmlEncode(field.Value)}</div>"));

}
