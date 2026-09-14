using System.Globalization;
using System.Net;
using Auraly.Contracts.Sales;

namespace Auraly.Pos.Printing;

public sealed class CreditSaleAcknowledgementRenderer
{
    public string Render(
        CreditSaleAcknowledgement value,
        string format,
        int receiptPaperWidthMillimeters = 80)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.DocumentId == Guid.Empty || value.CreditAmount <= 0 ||
            string.IsNullOrWhiteSpace(value.DocumentNumber) ||
            string.IsNullOrWhiteSpace(value.CustomerName) ||
            string.IsNullOrWhiteSpace(value.CustomerIdentification) ||
            string.IsNullOrWhiteSpace(value.SoldByName))
            throw new ArgumentException(
                "El comprobante de venta a crédito no contiene todos los datos obligatorios.",
                nameof(value));

        return format switch
        {
            "Receipt" => RenderReceipt(value, receiptPaperWidthMillimeters),
            "HalfLetter" => RenderSheet(value, "215.9mm 139.7mm", "media-carta"),
            "HalfLegal" => RenderSheet(value, "215.9mm 165.1mm", "media-oficio"),
            "Letter" => RenderSheet(value, "Letter portrait", "carta"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format), "El formato del comprobante de crédito no es válido.")
        };
    }

    private static string RenderReceipt(
        CreditSaleAcknowledgement value,
        int paperWidthMillimeters)
    {
        if (paperWidthMillimeters is not (58 or 80))
            throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters));
        return Document(
            value,
            $"{paperWidthMillimeters}mm auto",
            $"width:{paperWidthMillimeters}mm;margin:0;padding:5mm 3mm 2mm 2mm;font:11px/1.4 ui-monospace,Consolas,monospace",
            "receipt");
    }

    private static string RenderSheet(
        CreditSaleAcknowledgement value,
        string pageSize,
        string formatClass) =>
        Document(
            value,
            pageSize,
            "max-width:185mm;margin:0 auto;padding:14mm 16mm;font:12px/1.45 Arial,sans-serif",
            formatClass);

    private static string Document(
        CreditSaleAcknowledgement value,
        string pageSize,
        string bodyStyle,
        string formatClass)
    {
        var template = PosPrintTemplateCatalog.CreditSaleAcknowledgement;
        var companyName = string.IsNullOrWhiteSpace(value.CompanyName)
            ? "Auraly"
            : value.CompanyName;
        var logo = string.IsNullOrWhiteSpace(value.CompanyLogoSource)
            ? string.Empty
            : $"<img class=\"logo\" src=\"{Encode(value.CompanyLogoSource)}\" alt=\"Logo de {Encode(companyName)}\">";
        var location = string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(value.BusinessName) ? null : value.BusinessName,
            string.IsNullOrWhiteSpace(value.WarehouseName) ? null : value.WarehouseName
        }.Where(item => item is not null));
        var locationMarkup = location.Length == 0
            ? string.Empty
            : $"<p class=\"scope\">{Encode(location)}</p>";
        var remaining = value.RemainingCredit is { } amount
            ? Money(amount)
            : "No disponible";

        return $$"""
<!doctype html><html lang="es" data-auraly-report="{{template.Code}}" data-auraly-report-version="{{template.Version}}" data-auraly-format="{{formatClass}}"><head><meta charset="utf-8"><title>Constancia de venta a crédito {{Encode(value.DocumentNumber)}}</title><style>@page{size:{{pageSize}};margin:0}*{box-sizing:border-box}body{color:#111;{{bodyStyle}}}header{text-align:center;border-bottom:1px solid #0f766e;padding-bottom:9px}.logo{display:block;max-width:48mm;max-height:18mm;object-fit:contain;margin:0 auto 3mm}h1{margin:2px 0;font-size:20px;text-transform:uppercase}h2{margin:5px 0 2px;font-size:13px;text-transform:uppercase;color:#0f766e}.scope{margin:3px 0;color:#475569}.intro{margin:14px 0}.details{width:100%;border-collapse:collapse}.details td{padding:6px 4px;border-bottom:1px solid #d6e2e1}.details td:last-child{text-align:right;font-weight:700}.amount{margin:16px 0;border:2px solid #0f766e;border-radius:8px;padding:10px}.amount div{display:flex;justify-content:space-between;gap:12px;padding:3px 0}.amount .credit{font-size:16px;font-weight:800}.signature{margin-top:28mm;display:grid;grid-template-columns:1fr 1fr;gap:16mm}.signature div{border-top:1px solid #111;padding-top:5px;text-align:center}.notice{margin-top:12px;font-size:10px;color:#475569;text-align:center}.footer{margin-top:10px;text-align:center;font-weight:700;color:#0f766e}</style></head><body><header>{{logo}}<h1>{{Encode(companyName)}}</h1><h2>Constancia de venta a crédito</h2>{{locationMarkup}}</header><p class="intro">El cliente declara haber recibido a satisfacción los productos relacionados en la factura indicada y reconoce el valor financiado.</p><table class="details"><tbody><tr><td>Factura</td><td>{{Encode(value.DocumentNumber)}}</td></tr><tr><td>Cliente</td><td>{{Encode(value.CustomerName)}}</td></tr><tr><td>Identificación</td><td>{{Encode(value.CustomerIdentification)}}</td></tr><tr><td>Fecha y hora</td><td>{{Date(value.IssuedAt)}}</td></tr><tr><td>Atendido por</td><td>{{Encode(value.SoldByName)}}</td></tr></tbody></table><section class="amount"><div class="credit"><span>Valor a crédito</span><span>{{Money(value.CreditAmount)}}</span></div><div><span>Cupo restante</span><span>{{remaining}}</span></div></section><section class="signature"><div>Firma del cliente</div><div>Identificación</div></section><p class="notice">Esta constancia acompaña la factura {{Encode(value.DocumentNumber)}} y no la reemplaza.</p><footer class="footer">Comprobante emitido por Auraly · www.auralyapp.co</footer></body></html>
""";
    }

    private static string Date(DateTimeOffset value) =>
        value.ToLocalTime().ToString(
            "dd/MM/yyyy hh:mm tt", CultureInfo.GetCultureInfo("es-CO"));

    private static string Money(decimal value) =>
        value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
