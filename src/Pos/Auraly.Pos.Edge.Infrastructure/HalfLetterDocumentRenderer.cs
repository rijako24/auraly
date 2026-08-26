using System.Globalization;
using System.Net;
using Auraly.Contracts.Sales;
using QRCoder;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed class HalfLetterDocumentRenderer
{
    private static readonly CultureInfo ColombianCulture =
        CultureInfo.GetCultureInfo("es-CO");

    public string Render(IReadOnlyCollection<OnlineSalesReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        if (receipts.Count == 0)
            throw new ArgumentException("At least one document is required.", nameof(receipts));

        var pages = string.Join("", receipts.Select(RenderPage));
        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <title>Documentos de venta</title>
              <style>
                @page { size: Letter portrait; margin: 0; }
                * { box-sizing: border-box; }
                html, body { margin: 0; color: #07111f; font-family: Arial, sans-serif; }
                .sheet { width: 215.9mm; height: 279.4mm; page-break-after: always; position: relative; overflow: hidden; }
                .sheet:last-child { page-break-after: auto; }
                .copy { height: 50%; padding: 8mm 10mm 6mm; overflow: hidden; position: relative; }
                .copy:first-child { border-bottom: 1px dashed #64748b; }
                .cut { position: absolute; left: 50%; top: calc(50% - 2.5mm); z-index: 2; padding: 0 2mm; transform: translateX(-50%); background: white; color: #64748b; font-size: 7pt; }
                .document { transform-origin: top left; font-size: 8pt; line-height: 1.2; }
                .top { display: grid; grid-template-columns: 1fr auto; gap: 6mm; border-bottom: 1px solid #0f766e; padding-bottom: 2mm; }
                h1 { margin: 0; font-size: 14pt; color: #065f5b; }
                .brand-lockup { display: flex; align-items: center; gap: 3mm; }
                .brand-logo { max-width: 30mm; max-height: 16mm; object-fit: contain; }
                h2 { margin: 1mm 0 0; font-size: 9pt; }
                .number { text-align: right; }
                .meta { display: grid; grid-template-columns: 1fr 1fr; gap: 1mm 5mm; margin: 2mm 0; }
                .pair { display: flex; justify-content: space-between; gap: 3mm; }
                .pair span { color: #475569; }
                table { width: 100%; border-collapse: collapse; margin-top: 1.5mm; }
                th { padding: 1.2mm; background: #e9f7f5; text-align: left; font-size: 7pt; }
                td { padding: 1.1mm; border-bottom: 1px solid #e2e8f0; }
                .numeric { text-align: right; white-space: nowrap; }
                .bottom { display: grid; grid-template-columns: 1fr 44mm; gap: 4mm; margin-top: 2mm; }
                .totals { border: 1px solid #cbd5e1; border-radius: 2mm; padding: 2mm; }
                .total { font-size: 10pt; color: #065f5b; }
                .fiscal { overflow-wrap: anywhere; font-size: 6.5pt; }
                .financial { margin-top: 1.5mm; display: grid; gap: .7mm; }
                .financial > strong { margin-top: .7mm; color: #065f5b; }
                .qr { display: block; width: 27mm; height: 27mm; margin: 0 auto; }
                .caption { text-align: center; color: #64748b; font-size: 6.5pt; }
                @media screen { body { background: #e2e8f0; } .sheet { margin: 8mm auto; background: white; box-shadow: 0 4px 24px #0f172a33; } }
              </style>
            </head>
            <body>{{pages}}<script>
              for (const copy of document.querySelectorAll('.copy')) {
                const content = copy.querySelector('.document');
                const available = copy.clientHeight - 2;
                if (content.scrollHeight > available) {
                  const scale = Math.max(.62, available / content.scrollHeight);
                  content.style.transform = `scale(${scale})`;
                  content.style.width = `${100 / scale}%`;
                }
              }
              addEventListener('load', () => setTimeout(() => window.print(), 150));
            </script></body></html>
            """;
    }

    private static string RenderPage(OnlineSalesReceipt receipt)
    {
        var copy = RenderCopy(receipt);
        return $"<section class=\"sheet\"><div class=\"copy\">{copy}</div><span class=\"cut\">CORTE MEDIA CARTA</span><div class=\"copy\">{copy}</div></section>";
    }

    private static string RenderCopy(OnlineSalesReceipt receipt)
    {
        var documentName = receipt.DocumentType == "SalesInvoice"
            ? "FACTURA ELECTRÓNICA DE VENTA"
            : "COMPROBANTE DE VENTA";
        var fiscalNumber = string.IsNullOrWhiteSpace(receipt.FiscalNumber)
            ? string.Empty
            : $"<div class=\"pair\"><span>Número DIAN</span><strong>{Encode(receipt.FiscalNumber)}</strong></div>";
        var rows = string.Join("", receipt.Lines.Select(line =>
            $"<tr><td>{Encode(line.ProductCode)} · {Encode(line.Description)}</td><td class=\"numeric\">{Quantity(line.Quantity)}</td><td class=\"numeric\">{Money(line.UnitPrice)}</td><td class=\"numeric\">{Money(line.Total)}</td></tr>"));
        var qr = string.IsNullOrWhiteSpace(receipt.QrPayload)
            ? string.Empty
            : $"<img class=\"qr\" alt=\"QR DIAN\" src=\"data:image/svg+xml;base64,{QrBase64(receipt.QrPayload)}\">";
        var cufe = string.IsNullOrWhiteSpace(receipt.Cufe)
            ? string.Empty
            : $"<div class=\"fiscal\"><strong>CUFE</strong><br>{Encode(receipt.Cufe)}</div>";
        var taxes = string.Join("", receipt.Lines
            .GroupBy(line => new { line.TaxCode, line.TaxRate })
            .Select(group => new
            {
                group.Key.TaxCode,
                group.Key.TaxRate,
                Base = group.Sum(line => line.Total - line.Tax),
                Amount = group.Sum(line => line.Tax)
            })
            .OrderBy(value => value.TaxCode, StringComparer.Ordinal)
            .ThenBy(value => value.TaxRate)
            .Select(tax => $"<div class=\"pair\"><span>{Encode(TaxName(tax.TaxCode))} {Rate(tax.TaxRate)}% · base {Money(tax.Base)}</span><strong>{Money(tax.Amount)}</strong></div>"));
        var payments = string.Join("", receipt.Payments.Select(payment =>
            $"<div class=\"pair\"><span>{Encode(PaymentName(payment.MethodCode))}</span><strong>{Money(payment.Amount)}</strong></div>"));
        var companyName = Encode(receipt.CompanyName);
        var companyLogo = string.IsNullOrWhiteSpace(receipt.CompanyLogoSource)
            ? string.Empty
            : $"<img class=\"brand-logo\" src=\"{Encode(receipt.CompanyLogoSource)}\" alt=\"Logo de {companyName}\">";

        return $$"""
          <article class="document">
            <header class="top"><div><div class="brand-lockup">{{companyLogo}}<h1>{{companyName}}</h1></div><h2>{{documentName}}</h2></div><div class="number"><strong>{{Encode(receipt.DocumentNumber)}}</strong><br>{{receipt.IssuedAt.ToString("yyyy-MM-dd HH:mm", ColombianCulture)}}</div></header>
            <section class="meta"><div class="pair"><span>Cliente</span><strong>{{Encode(receipt.CustomerName)}}</strong></div><div class="pair"><span>Identificación</span><strong>{{Encode(receipt.CustomerIdentification)}}</strong></div>{{fiscalNumber}}</section>
            <table><thead><tr><th>Producto</th><th class="numeric">Cant.</th><th class="numeric">Precio</th><th class="numeric">Total</th></tr></thead><tbody>{{rows}}</tbody></table>
            <section class="bottom"><div>{{cufe}}<div class="financial"><strong>Impuestos por tarifa</strong>{{taxes}}<strong>Medios de pago</strong>{{payments}}</div><div class="caption">Representación gráfica · copia cliente / control</div></div><div><div class="totals"><div class="pair"><span>Subtotal</span><strong>{{Money(receipt.UntaxedAmount)}}</strong></div><div class="pair"><span>Total impuestos</span><strong>{{Money(receipt.TaxAmount)}}</strong></div><div class="pair total"><span>Total</span><strong>{{Money(receipt.PayableAmount)}}</strong></div></div>{{qr}}</div></section>
          </article>
          """;
    }

    private static string QrBase64(string payload)
    {
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qr = new SvgQRCode(data);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(qr.GetGraphic(3)));
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Money(decimal value) => value.ToString("C0", ColombianCulture);
    private static string Quantity(decimal value) => value.ToString("0.###", ColombianCulture);
    private static string Rate(decimal value) => value.ToString("0.##", ColombianCulture);
    private static string TaxName(string code) => code switch { "01" => "IVA", "02" => "IC", "03" => "ICA", "04" => "INC", _ => code };
    private static string PaymentName(string code) => code switch { "Cash" => "Efectivo", "Card" => "Tarjeta", "DebitCard" => "Tarjeta débito", "CreditCard" => "Tarjeta crédito", "Transfer" => "Transferencia", "Deposit" => "Consignación", "Credit" => "Crédito / cartera", "Voucher" => "Bono / vale", "Check" => "Cheque", "Withholding" => "Retención", _ => code };
}
