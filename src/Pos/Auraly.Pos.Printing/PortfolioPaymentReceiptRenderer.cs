using System.Globalization;
using System.Net;
using System.Text;

namespace Auraly.Pos.Printing;

public sealed record PortfolioPaymentReceiptAllocation(string DocumentNumber, decimal Amount);
public sealed record PortfolioPaymentReceiptTender(string MethodName, decimal Amount, string? Reference);

public sealed record PortfolioPaymentReceipt(
    Guid PaymentId,
    string Direction,
    string DocumentNumber,
    DateTimeOffset PaidAt,
    string CompanyName,
    string? LegalName,
    string? Nit,
    string? VerificationDigit,
    string? CompanyLogoSource,
    string BusinessName,
    string? BusinessAddress,
    string? BusinessPhone,
    string PartyName,
    string PartyIdentification,
    string ResponsibleName,
    decimal TotalAmount,
    IReadOnlyList<PortfolioPaymentReceiptAllocation> Allocations,
    IReadOnlyList<PortfolioPaymentReceiptTender> Payments);

public sealed class PortfolioPaymentReceiptRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");

    public string Render(PortfolioPaymentReceipt receipt, int paperWidthMillimeters)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (paperWidthMillimeters is not (58 or 80) ||
            receipt.PaymentId == Guid.Empty ||
            receipt.Direction is not ("Receivable" or "Payable") ||
            string.IsNullOrWhiteSpace(receipt.DocumentNumber) ||
            string.IsNullOrWhiteSpace(receipt.CompanyName) ||
            string.IsNullOrWhiteSpace(receipt.PartyName) ||
            string.IsNullOrWhiteSpace(receipt.ResponsibleName) ||
            receipt.TotalAmount <= 0 ||
            receipt.Allocations is null or { Count: < 1 or > 100 } ||
            receipt.Payments is null or { Count: < 1 or > 10 } ||
            receipt.Allocations.Any(line => string.IsNullOrWhiteSpace(line.DocumentNumber) || line.Amount <= 0) ||
            receipt.Payments.Any(line => string.IsNullOrWhiteSpace(line.MethodName) || line.Amount <= 0) ||
            decimal.Round(receipt.Allocations.Sum(line => line.Amount), 4) !=
                decimal.Round(receipt.TotalAmount, 4) ||
            decimal.Round(receipt.Payments.Sum(line => line.Amount), 4) !=
                decimal.Round(receipt.TotalAmount, 4))
            throw new ArgumentException("El comprobante de pago no contiene datos consistentes.", nameof(receipt));

        var incoming = receipt.Direction == "Receivable";
        var template = incoming
            ? PosPrintTemplateCatalog.ReceivablePayment
            : PosPrintTemplateCatalog.PayablePayment;
        var width = paperWidthMillimeters == 58 ? 50 : 72;
        var date = receipt.PaidAt.ToOffset(TimeSpan.FromHours(-5));
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html><html lang="es" data-auraly-report="{{template.Code}}" data-auraly-report-version="{{template.Version}}"><head><meta charset="utf-8"><title>{{(incoming ? "Recibo de abono a cartera" : "Comprobante de pago a proveedor")}}</title><style>
            @page{size:{{paperWidthMillimeters}}mm auto;margin:4mm}
            *{box-sizing:border-box}body{width:{{width}}mm;margin:0 auto;color:#17212b;font:11px/1.38 Arial,sans-serif}
            .brand{text-align:center;border-bottom:2px solid #0c7772;padding:2mm 0 3mm}
            .brand img{display:block;max-width:28mm;max-height:18mm;margin:0 auto 2mm;object-fit:contain}
            .brand h1{margin:0;font-size:17px;line-height:1.15;overflow-wrap:anywhere}
            .brand p{margin:2px 0;color:#40515c;overflow-wrap:anywhere}
            .kind{text-align:center;padding:3mm 0 2mm;border-bottom:1px dashed #87949d}
            .kind small{display:block;color:#0c7772;font-size:10px;font-weight:800;letter-spacing:.12em;text-transform:uppercase}
            .kind h2{margin:2px 0 0;font-size:15px;line-height:1.2}
            .block{padding:3mm 0;border-bottom:1px dashed #a7b2b8}
            .label{display:block;color:#596a73;font-size:9px;font-weight:700;letter-spacing:.06em;text-transform:uppercase}
            .value{font-weight:700;overflow-wrap:anywhere}
            .grid{display:grid;grid-template-columns:1fr 1fr;gap:2mm}
            h3{margin:0 0 2mm;font-size:11px;color:#0a625e;text-transform:uppercase;letter-spacing:.06em}
            .row{display:flex;justify-content:space-between;gap:3mm;margin:1.5mm 0}
            .row span:first-child{min-width:0;overflow-wrap:anywhere}
            .row strong{text-align:right;white-space:nowrap}
            .ref{display:block;color:#596a73;font-size:9px;overflow-wrap:anywhere}
            .total{display:flex;justify-content:space-between;align-items:center;gap:2mm;margin-top:3mm;padding:2.5mm 0;border-top:2px solid #0c7772;border-bottom:2px solid #0c7772;font-weight:800}
            .total span,.total strong{white-space:nowrap}.total span{font-size:{{(paperWidthMillimeters == 58 ? 12 : 15)}}px}.total strong{font-size:{{(paperWidthMillimeters == 58 ? 13 : 15)}}px}
            .signature{margin-top:19mm;border-top:1px solid #17212b;text-align:center;padding-top:1.5mm}
            .signature-note{text-align:center;color:#596a73;margin-top:2mm}
            footer{text-align:center;color:#596a73;margin-top:5mm;font-size:9px}
            </style></head><body><header class="brand">
            """);
        if (Uri.TryCreate(receipt.CompanyLogoSource, UriKind.Absolute, out var logo) &&
            (logo.Scheme == Uri.UriSchemeHttps || logo.Scheme == "data"))
            html.Append($"<img src=\"{E(receipt.CompanyLogoSource!)}\" alt=\"Logo de la empresa\">");
        html.Append($"<h1>{E(receipt.CompanyName)}</h1>");
        if (!string.IsNullOrWhiteSpace(receipt.LegalName) &&
            !string.Equals(receipt.LegalName, receipt.CompanyName, StringComparison.OrdinalIgnoreCase))
            html.Append($"<p>{E(receipt.LegalName)}</p>");
        if (!string.IsNullOrWhiteSpace(receipt.Nit))
            html.Append($"<p>NIT {E(receipt.Nit)}{(string.IsNullOrWhiteSpace(receipt.VerificationDigit) ? "" : $"-{E(receipt.VerificationDigit)}")}</p>");
        html.Append($"<p>{E(receipt.BusinessName)}</p>");
        if (!string.IsNullOrWhiteSpace(receipt.BusinessAddress))
            html.Append($"<p>{E(receipt.BusinessAddress)}</p>");
        if (!string.IsNullOrWhiteSpace(receipt.BusinessPhone))
            html.Append($"<p>Tel. {E(receipt.BusinessPhone)}</p>");
        html.Append($$"""
            </header><section class="kind"><small>{{(incoming ? "Entrada de dinero" : "Salida de dinero")}}</small>
            <h2>{{(incoming ? "Recibo de abono a cartera" : "Pago a proveedor")}}</h2></section>
            <section class="block grid"><div><span class="label">Comprobante</span><span class="value">{{E(receipt.DocumentNumber)}}</span></div>
            <div><span class="label">Fecha y hora</span><span class="value">{{date.ToString("dd/MM/yyyy HH:mm", Culture)}}</span></div></section>
            <section class="block"><span class="label">{{(incoming ? "Recibido de" : "Pagado a")}}</span><span class="value">{{E(receipt.PartyName)}}</span>
            <div>{{E(receipt.PartyIdentification)}}</div></section>
            <section class="block"><h3>Facturas aplicadas</h3>
            """);
        foreach (var line in receipt.Allocations)
            html.Append($"<div class=\"row\"><span>{E(line.DocumentNumber)}</span><strong>{Money(line.Amount)}</strong></div>");
        html.Append("</section><section class=\"block\"><h3>Medios de pago</h3>");
        foreach (var line in receipt.Payments)
        {
            html.Append($"<div class=\"row\"><span>{E(line.MethodName)}</span><strong>{Money(line.Amount)}</strong></div>");
            if (!string.IsNullOrWhiteSpace(line.Reference))
                html.Append($"<span class=\"ref\">Referencia: {E(line.Reference)}</span>");
        }
        html.Append($$"""
            </section><div class="total"><span>Valor {{(incoming ? "recibido" : "pagado")}}</span><strong>{{Money(receipt.TotalAmount)}}</strong></div>
            <section class="block"><span class="label">Registrado por</span><span class="value">{{E(receipt.ResponsibleName)}}</span></section>
            """);
        if (!incoming)
            html.Append($"<div class=\"signature\">Firma de recibido · {E(receipt.PartyName)}</div><p class=\"signature-note\">Documento: {E(receipt.PartyIdentification)}</p>");
        else
            html.Append("<p class=\"signature-note\">Conserve este comprobante de su abono.</p>");
        html.Append("<footer><strong>www.auralyapp.co</strong></footer></body></html>");
        return html.ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Money(decimal value) => value.ToString(
        value == decimal.Truncate(value) ? "C0" : "C2", Culture);
}
