using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Auraly.Contracts.Sales;
using QRCoder;

namespace Auraly.Pos.Edge.Infrastructure;

public interface IReceiptPreviewLauncher
{
    Task OpenAsync(string absolutePath, CancellationToken cancellationToken = default);
}

public sealed class ShellReceiptPreviewLauncher : IReceiptPreviewLauncher
{
    public Task OpenAsync(
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The local receipt preview requires a desktop operating system.");

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(absolutePath),
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}

public sealed class HtmlReceiptPreviewRenderer
{
    private static readonly CultureInfo ColombianCulture =
        CultureInfo.GetCultureInfo("es-CO");

    public string Render(PosReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.PaperWidthMillimeters is not (58 or 80))
            throw new ArgumentOutOfRangeException(
                nameof(receipt),
                "Receipt width must be 58 or 80 mm.");

        var isFiscal = PosSaleDocumentTypes.IsFiscal(receipt.DocumentType);
        var template = PosPrintTemplateCatalog.ForSale(receipt.DocumentType);
        var bodyFontSize = isFiscal ? 12 : 11;
        var issuedBy = isFiscal
            ? "Factura emitida por Auraly"
            : "Comprobante emitido por Auraly";
        var displayNumber = Encode(PosReceiptPresentation.DisplayNumber(receipt));
        var documentHeader = isFiscal
            ? $"<div class=\"title\">N.º {displayNumber}</div>"
            : $"<div class=\"title\">{Encode(PosReceiptPresentation.Title(receipt))}</div><div class=\"title\">N.º {displayNumber}</div>";
        var qrSvg = string.Empty;
        if (isFiscal)
        {
            using var qrData = QRCodeGenerator.GenerateQrCode(
                receipt.QrPayload!, QRCodeGenerator.ECCLevel.Q);
            using var qr = new SvgQRCode(qrData);
            qrSvg = qr.GetGraphic(
                pixelsPerModule: 4, darkColorHex: "#061f22", lightColorHex: "#ffffff",
                drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
        }
        var fiscalFooter = isFiscal
            ? $"<div class=\"cufe\"><strong>CUFE</strong><br>{Encode(receipt.Cufe!)}</div><div class=\"qr\">{qrSvg}</div>"
            : string.Empty;

        var lines = string.Join(
            Environment.NewLine,
            receipt.Lines.Select(RenderLine));
        var payments = string.Join(
            Environment.NewLine,
            receipt.Payments.Select(payment =>
                Pair(PaymentName(payment.MethodCode), Money(payment.Amount))));
        var taxes = string.Join(
            Environment.NewLine,
            receipt.Lines
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
                .Select(tax =>
                    $"<tr><td>{Encode(TaxName(tax.TaxCode))} {Rate(tax.TaxRate)}%</td>" +
                    $"<td>{Money(tax.Base)}</td><td>{Money(tax.Amount)}</td></tr>"));
        var withholdingLines = string.Join(
            Environment.NewLine,
            (receipt.Withholdings ?? []).Select(withholding =>
                Pair($"Ret. {withholding.Name} ({Rate(withholding.Rate)}%)", $"-{Money(withholding.Amount)}")));
        var withholdings = receipt.WithholdingTotal <= 0
            ? string.Empty
            : $"{Pair("Total bruto", Money(receipt.PayableAmount))}<div class=\"section-title\">Retenciones</div>{withholdingLines}{Pair("Total retenciones", $"-{Money(receipt.WithholdingTotal)}")}";
        var netPayable = receipt.WithholdingTotal > 0
            ? receipt.NetPayableAmount
            : receipt.PayableAmount;

        var companyName = Encode(receipt.CompanyName ?? string.Empty);
        var scope = Scope(receipt.BusinessName);
        var companyLogo = string.IsNullOrWhiteSpace(receipt.CompanyLogoSource)
            ? string.Empty
            : $"<img class=\"brand-logo\" src=\"{Encode(receipt.CompanyLogoSource)}\" alt=\"Logo de {companyName}\">";
        return $$"""
            <!doctype html>
            <html lang="es" data-auraly-report="{{template.Code}}" data-auraly-report-version="{{template.Version}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{companyName}} {{Encode(receipt.DocumentNumber)}}</title>
              <style>
                @page { size: {{receipt.PaperWidthMillimeters}}mm auto; margin: 3mm; }
                * { box-sizing: border-box; }
                html { background: #e9eeee; }
                body {
                  width: {{receipt.PaperWidthMillimeters}}mm;
                  margin: 18px auto;
                  background: white;
                  color: #061f22;
                  font: {{bodyFontSize}}px/1.35 ui-monospace, "Cascadia Mono", Consolas, monospace;
                  box-shadow: 0 16px 45px rgba(6,31,34,.18);
                }
                .receipt { padding: 5mm 3mm 2mm 2mm; }
                .center { text-align: center; }
                header > * + * { margin-top: 5px; }
                .brand { font: 800 23px/1 system-ui, sans-serif; letter-spacing: -.04em; text-transform: uppercase; }
                .brand-logo { display: block; max-width: 48mm; max-height: 18mm; object-fit: contain; margin: 0 auto 3mm; }
                .title { margin-top: 6px; font-size: 13px; font-weight: 800; text-transform: uppercase; }
                .scope { margin-top: 4px; overflow-wrap: anywhere; }
                .muted { color: #49666a; }
                .rule { border: 0; border-top: 1px dashed #789093; margin: 8px 0; }
                .line { margin: 9px 0; break-inside: avoid; }
                .product { font-weight: 800; overflow-wrap: anywhere; }
                .pair { display: flex; justify-content: space-between; gap: 10px; }
                .pair strong, .amount { white-space: nowrap; font-variant-numeric: tabular-nums; }
                .discount { color: #9a4b08; font-size: 11px; }
                .section-title { margin: 7px 0 3px; font-weight: 900; text-transform: uppercase; }
                .tax-table { width: 100%; border-collapse: collapse; margin: 4px 0; }
                .tax-table th { padding: 3px 0; border-bottom: 1px solid #789093; text-align: right; font-size: 10px; }
                .tax-table th:first-child, .tax-table td:first-child { text-align: left; }
                .tax-table td { padding: 3px 0; text-align: right; font-variant-numeric: tabular-nums; }
                .total { margin-top: 5px; font-size: 16px; font-weight: 900; }
                .cufe { overflow-wrap: anywhere; font-size: 9px; }
                .qr { width: 38mm; max-width: 100%; margin: 6px auto 4px; }
                .qr svg { display: block; width: 100%; height: auto; }
                .platform-footer { margin-top: 10px; text-align: center; font: 10px/1.4 system-ui, sans-serif; color: #49666a; }
                .platform-footer strong { color: #065f5b; font-size: 12px; }
                .actions {
                  position: sticky;
                  top: 0;
                  display: flex;
                  justify-content: center;
                  padding: 10px;
                  background: #061f22;
                }
                .actions button {
                  border: 0;
                  border-radius: 8px;
                  background: #62e6dd;
                  color: #061f22;
                  padding: 9px 15px;
                  font: 700 13px system-ui, sans-serif;
                  cursor: pointer;
                }
                @media print {
                  html, body { background: white; margin: 0; box-shadow: none; }
                  .actions { display: none; }
                  .receipt { padding: 0; }
                }
              </style>
            </head>
            <body>
              <div class="actions"><button type="button" onclick="window.print()">Imprimir tirilla</button></div>
              <main class="receipt">
                <header class="center">
                  {{companyLogo}}
                  <div class="brand">{{companyName}}</div>
                  {{documentHeader}}
                  <div class="muted">{{Encode(receipt.IssuedAt.ToLocalTime().ToString("dd/MM/yyyy, h:mm:ss tt", ColombianCulture))}}</div>
                  {{(string.IsNullOrWhiteSpace(scope) ? string.Empty : $"<div class=\"scope muted\">{Encode(scope)}</div>")}}
                </header>
                <hr class="rule">
                <div class="pair"><span>Cliente</span><strong>{{Encode(receipt.CustomerName ?? receipt.CustomerIdentification)}}</strong></div>
                <div class="pair"><span>Identificación</span><strong>{{Encode(receipt.CustomerIdentification)}}</strong></div>
                <hr class="rule">
                {{lines}}
                <hr class="rule">
                <div class="section-title">Resumen e impuestos</div>
                <table class="tax-table"><thead><tr><th>Impuesto</th><th>Base</th><th>Valor</th></tr></thead><tbody>{{taxes}}</tbody></table>
                {{Pair("Subtotal", Money(receipt.UntaxedAmount))}}
                {{Pair("Total impuestos", Money(receipt.TaxAmount))}}
                {{withholdings}}
                <hr class="rule">
                <div class="pair total"><span>Total</span><strong>{{Money(netPayable)}}</strong></div>
                <hr class="rule">
                <div class="section-title">Medios de pago</div>
                {{payments}}
                <hr class="rule">
                {{fiscalFooter}}
                <footer class="platform-footer">{{issuedBy}}<br><strong>www.auralyapp.co</strong></footer>
              </main>
              <script>
                window.addEventListener("load", () => window.setTimeout(() => window.print(), 250));
              </script>
            </body>
            </html>
            """;
    }

    private static string RenderLine(PosReceiptLine line)
    {
        var product = Encode(line.Description);
        var discount = line.Discount > 0
            ? $"<div class=\"pair discount\"><span>Descuento</span><strong>-{Money(line.Discount)}</strong></div>"
            : string.Empty;
        return $$"""
            <section class="line">
              <div class="product">{{product}}</div>
              <div class="pair muted">
                <span>{{Quantity(line.Quantity)}} × {{Money(line.UnitPrice)}}</span>
                <strong>{{Money(line.Total)}}</strong>
              </div>
              {{discount}}
            </section>
            """;
    }

    private static string Pair(string label, string value) =>
        $"<div class=\"pair\"><span>{Encode(label)}</span><strong>{value}</strong></div>";

    private static string Scope(string? businessName)
    {
        return string.IsNullOrWhiteSpace(businessName) ? string.Empty : $"Sede: {businessName}";
    }

    private static string PaymentName(string code) => code switch
    {
        "Cash" => "Efectivo",
        "DebitCard" => "Tarjeta débito",
        "CreditCard" => "Tarjeta crédito",
        "Transfer" => "Transferencia",
        "Credit" => "Crédito / cartera",
        "Voucher" => "Bono / vale",
        "Check" => "Cheque",
        "Withholding" => "Retención",
        _ => code
    };

    private static string Money(decimal value) =>
        value.ToString("C0", ColombianCulture);

    private static string Quantity(decimal value) =>
        value.ToString("0.###", ColombianCulture);

    private static string Rate(decimal value) =>
        value.ToString("0.##", ColombianCulture);

    private static string TaxName(string code) => code switch
    {
        "01" => "IVA",
        "02" => "IC",
        "03" => "ICA",
        "04" => "INC",
        _ => code
    };

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}

public sealed class HtmlReceiptPreviewPrinter(
    string outputDirectory,
    HtmlReceiptPreviewRenderer renderer,
    IReceiptPreviewLauncher launcher) : IPosReceiptPrinter
{
    public async Task PrintAsync(
        PosReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException(
                "A receipt preview output directory must be configured.");

        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"{receipt.PrintJobId:N}.html");
        var temporary = Path.Combine(
            directory,
            $".{receipt.PrintJobId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                renderer.Render(receipt),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        await launcher.OpenAsync(target, cancellationToken);
    }
}
