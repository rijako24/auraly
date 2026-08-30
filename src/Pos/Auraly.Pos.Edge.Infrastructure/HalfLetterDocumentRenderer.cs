using System.Globalization;
using System.Net;
using Auraly.Contracts.Sales;
using QRCoder;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed class HalfLetterDocumentRenderer
{
    public const string HalfLetter = "HalfLetter";
    public const string HalfLegal = "HalfLegal";
    public const string Letter = "Letter";

    private static readonly CultureInfo ColombianCulture =
        CultureInfo.GetCultureInfo("es-CO");

    public string Render(
        IReadOnlyCollection<OnlineSalesReceipt> receipts,
        string format = HalfLetter)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        if (receipts.Count == 0)
            throw new ArgumentException("At least one document is required.", nameof(receipts));
        if (format is not (HalfLetter or HalfLegal or Letter))
            throw new ArgumentOutOfRangeException(nameof(format), "The document format is not supported.");

        var isLetter = format == Letter;
        var pageSize = format == HalfLegal
            ? "215.9mm 330.2mm"
            : "Letter portrait";
        var sheetClass = format switch
        {
            HalfLegal => "half half-oficio",
            HalfLetter => "half half-letter",
            _ => "letter"
        };
        var pages = string.Join("", receipts.Select(receipt =>
            RenderPage(receipt, sheetClass, isLetter)));

        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <title>Documentos de venta</title>
              <style>
                @page { size: {{pageSize}}; margin: 0; }
                * { box-sizing: border-box; }
                html, body { margin: 0; color: #07111f; font-family: Arial, sans-serif; }
                .sheet { width: 215.9mm; page-break-after: always; position: relative; overflow: hidden; background: white; }
                .sheet:last-child { page-break-after: auto; }
                .half-letter { height: 279.4mm; --copy-height: 139.7mm; --document-width: 129.7mm; }
                .half-oficio { height: 330.2mm; --copy-height: 165.1mm; --document-width: 155.1mm; }
                .letter { height: 279.4mm; }
                .copy { position: relative; overflow: hidden; }
                .half .copy { width: 215.9mm; height: var(--copy-height); }
                .half .document {
                  position: absolute; left: 50%; top: 50%;
                  width: var(--document-width); height: 203.9mm;
                  transform: translate(-50%, -50%) rotate(90deg);
                  transform-origin: center; padding: 5mm 6mm 4mm;
                }
                .letter .copy { width: 100%; height: 100%; padding: 12mm 13mm 10mm; }
                .letter .document { width: 100%; height: 100%; }
                .document { font-size: 8pt; line-height: 1.22; }
                .document-content { min-height: 100%; display: flex; flex-direction: column; transform-origin: top left; }
                .top { display: grid; grid-template-columns: 1fr auto; align-items: start; gap: 5mm; border-bottom: .25mm solid #0f766e; padding-bottom: 1.7mm; }
                .brand-lockup { display: flex; align-items: center; gap: 2.5mm; min-width: 0; }
                .brand-logo { max-width: 28mm; max-height: 13mm; object-fit: contain; }
                h1 { margin: 0; font-size: 13pt; font-weight: 500; color: #065f5b; overflow-wrap: anywhere; }
                h2 { margin: .8mm 0 0; font-size: 8.5pt; }
                .number { text-align: right; white-space: nowrap; }
                .meta { display: grid; grid-template-columns: 1.2fr 1fr; gap: .8mm 4mm; margin: 1.5mm 0 1mm; }
                .pair { display: flex; justify-content: space-between; gap: 2.5mm; }
                .pair span { color: #475569; }
                table { width: 100%; border-collapse: collapse; margin-top: 1mm; }
                th { padding: 1.1mm; background: #eef8f7; text-align: left; font-size: 7pt; }
                td { padding: 1mm 1.1mm; border-bottom: .2mm solid #e2e8f0; }
                .numeric { text-align: right; white-space: nowrap; }
                .details { display: grid; grid-template-columns: minmax(0, 1fr) 41mm; gap: 4mm; margin-top: 2mm; }
                .fiscal { overflow-wrap: anywhere; font-size: 6.2pt; }
                .breakdowns { display: grid; grid-template-columns: 1fr 1fr; gap: 3mm; margin-top: 1.5mm; }
                .breakdown { min-width: 0; }
                .breakdown-title { margin-bottom: .7mm; color: #065f5b; font-weight: 700; }
                .breakdown .pair { padding: .25mm 0; font-size: 6.7pt; }
                .totals { border: .2mm solid #cbd5e1; border-radius: 2mm; padding: 2mm; }
                .total { margin-top: .7mm; font-size: 10pt; color: #065f5b; }
                .qr { display: block; width: 25mm; height: 25mm; margin: 1mm auto 0; }
                .caption { margin-top: 1mm; color: #64748b; font-size: 6.2pt; }
                .footer { display: grid; grid-template-columns: 1fr auto auto; align-items: end; gap: 3mm; margin-top: auto; padding-top: 1.5mm; border-top: .2mm solid #94a3b8; color: #64748b; font-size: 6.2pt; }
                .platform { text-align: center; color: #334155; }
                .platform strong { color: #065f5b; }
                .page-number { white-space: nowrap; text-align: right; }
                .letter .document { font-size: 9pt; }
                .letter h1 { font-size: 16pt; }
                .letter h2 { font-size: 10pt; }
                .letter .meta { margin-top: 2.5mm; }
                .letter th { font-size: 8pt; }
                .letter td { padding-top: 1.6mm; padding-bottom: 1.6mm; }
                .letter .details { margin-top: 4mm; grid-template-columns: minmax(0, 1fr) 49mm; }
                .letter .breakdown .pair { font-size: 7.5pt; }
                .letter .fiscal, .letter .caption, .letter .footer { font-size: 7pt; }
                .letter .qr { width: 33mm; height: 33mm; }
                @media screen { body { background: #e2e8f0; } .sheet { margin: 8mm auto; box-shadow: 0 4px 24px #0f172a33; } }
              </style>
            </head>
            <body>{{pages}}<script>
              for (const documentElement of document.querySelectorAll('.document')) {
                const content = documentElement.querySelector('.document-content');
                const available = documentElement.clientHeight;
                if (content.scrollHeight > available) {
                  const scale = Math.max(.58, available / content.scrollHeight);
                  content.style.transform = `scale(${scale})`;
                  content.style.width = `${100 / scale}%`;
                }
              }
              addEventListener('load', () => setTimeout(() => window.print(), 150));
            </script></body></html>
            """;
    }

    private static string RenderPage(OnlineSalesReceipt receipt, string sheetClass, bool isLetter)
    {
        var copy = RenderCopy(receipt);
        return isLetter
            ? $"<section class=\"sheet {sheetClass}\"><div class=\"copy\">{copy}</div></section>"
            : $"<section class=\"sheet {sheetClass}\"><div class=\"copy\">{copy}</div><div class=\"copy\">{copy}</div></section>";
    }

    private static string RenderCopy(OnlineSalesReceipt receipt)
    {
        var isInvoice = receipt.DocumentType == PosSaleDocumentTypes.Invoice;
        var documentName = isInvoice
            ? "FACTURA ELECTRÓNICA DE VENTA"
            : "COMPROBANTE DE VENTA";
        var representationName = isInvoice
            ? "Representación gráfica de factura electrónica"
            : "Representación gráfica del comprobante de venta";
        var issuedBy = isInvoice
            ? "Factura emitida por Auraly"
            : "Comprobante emitido por Auraly";
        var fiscalNumber = !isInvoice || string.IsNullOrWhiteSpace(receipt.FiscalNumber)
            ? string.Empty
            : $"<div class=\"pair\"><span>Número DIAN</span><strong>{Encode(receipt.FiscalNumber)}</strong></div>";
        var rows = string.Join("", receipt.Lines.Select(line =>
            $"<tr><td>{Encode(line.ProductCode)} · {Encode(line.Description)}</td><td class=\"numeric\">{Quantity(line.Quantity)}</td><td class=\"numeric\">{Money(line.UnitPrice)}</td><td class=\"numeric\">{Money(line.Total)}</td></tr>"));
        var qr = !isInvoice || string.IsNullOrWhiteSpace(receipt.QrPayload)
            ? string.Empty
            : $"<img class=\"qr\" alt=\"QR DIAN\" src=\"data:image/svg+xml;base64,{QrBase64(receipt.QrPayload)}\">";
        var cufe = !isInvoice || string.IsNullOrWhiteSpace(receipt.Cufe)
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
        var withholdings = string.Join("", (receipt.Withholdings ?? []).Select(withholding =>
            $"<div class=\"pair\"><span>Ret. {Encode(withholding.Name)} ({Rate(withholding.Rate)}%)</span><strong>-{Money(withholding.Amount)}</strong></div>"));
        var withholdingTotals = receipt.WithholdingTotal <= 0 ? string.Empty :
            $"{withholdings}<div class=\"pair\"><span>Total retenciones</span><strong>-{Money(receipt.WithholdingTotal)}</strong></div>";
        var netPayable = receipt.WithholdingTotal > 0 ? receipt.NetPayableAmount : receipt.PayableAmount;
        var companyName = Encode(receipt.CompanyName);
        var companyLogo = string.IsNullOrWhiteSpace(receipt.CompanyLogoSource)
            ? string.Empty
            : $"<img class=\"brand-logo\" src=\"{Encode(receipt.CompanyLogoSource)}\" alt=\"Logo de {companyName}\">";
        var issuedAt = receipt.IssuedAt.ToString("d/M/yyyy, h:mm:ss tt", ColombianCulture);

        return $$"""
          <article class="document"><div class="document-content">
            <header class="top"><div><div class="brand-lockup">{{companyLogo}}<h1>{{companyName}}</h1></div><h2>{{documentName}}</h2></div><div class="number"><span>N.º de ticket</span><br><strong>{{Encode(receipt.DocumentNumber)}}</strong><br>{{issuedAt}}</div></header>
            <section class="meta"><div class="pair"><span>Cliente</span><strong>{{Encode(receipt.CustomerName)}}</strong></div><div class="pair"><span>Identificación</span><strong>{{Encode(receipt.CustomerIdentification)}}</strong></div>{{fiscalNumber}}</section>
            <table><thead><tr><th>Producto</th><th class="numeric">Cant.</th><th class="numeric">Precio</th><th class="numeric">Total</th></tr></thead><tbody>{{rows}}</tbody></table>
            <section class="details"><div>{{cufe}}<div class="breakdowns"><section class="breakdown"><div class="breakdown-title">Impuestos por tarifa</div>{{taxes}}</section><section class="breakdown"><div class="breakdown-title">Medios de pago</div>{{payments}}</section></div><div class="caption">Representación gráfica · copia cliente / control</div></div><div><div class="totals"><div class="pair"><span>Subtotal</span><strong>{{Money(receipt.UntaxedAmount)}}</strong></div><div class="pair"><span>Total impuestos</span><strong>{{Money(receipt.TaxAmount)}}</strong></div><div class="pair"><span>Total bruto</span><strong>{{Money(receipt.PayableAmount)}}</strong></div>{{withholdingTotals}}<div class="pair total"><span>Total a pagar</span><strong>{{Money(netPayable)}}</strong></div>{{qr}}</div></div></section>
            <footer class="footer"><span>{{representationName}}</span><span class="platform">{{issuedBy}} · <strong>www.auralyapp.co</strong><br>Emitido: {{issuedAt}}</span><span class="page-number">Página 1 de 1</span></footer>
          </div></article>
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
    private static string PaymentName(string code) => code switch { "Cash" => "Efectivo", "Card" => "Tarjeta", "DebitCard" => "Tarjeta débito", "CreditCard" => "Tarjeta crédito", "Transfer" => "Transferencia", "Credit" => "Crédito / cartera", "Voucher" => "Bono / vale", "Check" => "Cheque", "Withholding" => "Retención", _ => code };
}
