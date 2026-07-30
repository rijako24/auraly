using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
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

        using var qrData = QRCodeGenerator.GenerateQrCode(
            receipt.QrPayload,
            QRCodeGenerator.ECCLevel.Q);
        using var qr = new SvgQRCode(qrData);
        var qrSvg = qr.GetGraphic(
            pixelsPerModule: 4,
            darkColorHex: "#061f22",
            lightColorHex: "#ffffff",
            drawQuietZones: true,
            sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);

        var lines = string.Join(
            Environment.NewLine,
            receipt.Lines.Select(RenderLine));
        var payments = string.Join(
            Environment.NewLine,
            receipt.Payments.Select(payment =>
                Pair(PaymentName(payment.MethodCode), Money(payment.Amount))));

        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Auraly {{Encode(receipt.DocumentNumber)}}</title>
              <style>
                @page { size: {{receipt.PaperWidthMillimeters}}mm auto; margin: 3mm; }
                * { box-sizing: border-box; }
                html { background: #e9eeee; }
                body {
                  width: {{receipt.PaperWidthMillimeters}}mm;
                  margin: 18px auto;
                  background: white;
                  color: #061f22;
                  font: 12px/1.35 ui-monospace, "Cascadia Mono", Consolas, monospace;
                  box-shadow: 0 16px 45px rgba(6,31,34,.18);
                }
                .receipt { padding: 5mm 4mm 7mm; }
                .center { text-align: center; }
                .brand { font: 800 23px/1 system-ui, sans-serif; letter-spacing: -.04em; }
                .title { margin-top: 5px; font-weight: 800; text-transform: uppercase; }
                .muted { color: #49666a; }
                .rule { border: 0; border-top: 1px dashed #789093; margin: 10px 0; }
                .line { margin: 9px 0; break-inside: avoid; }
                .product { font-weight: 800; overflow-wrap: anywhere; }
                .pair { display: flex; justify-content: space-between; gap: 10px; }
                .pair strong, .amount { white-space: nowrap; font-variant-numeric: tabular-nums; }
                .discount { color: #9a4b08; font-size: 11px; }
                .total { margin-top: 5px; font-size: 16px; font-weight: 900; }
                .cufe { overflow-wrap: anywhere; font-size: 9px; }
                .qr { width: 38mm; max-width: 100%; margin: 10px auto 5px; }
                .qr svg { display: block; width: 100%; height: auto; }
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
                  <div class="brand">Auraly</div>
                  <div class="title">Factura electrónica de venta</div>
                  <div class="muted">{{Encode(receipt.IssuedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))}}</div>
                </header>
                <hr class="rule">
                {{Pair("Documento Auraly", Encode(receipt.DocumentNumber))}}
                {{Pair("Número DIAN", Encode(receipt.FiscalNumber))}}
                <div class="pair"><span>Adquirente</span><strong>{{Encode(receipt.CustomerIdentification)}}</strong></div>
                <hr class="rule">
                {{lines}}
                <hr class="rule">
                {{Pair("Subtotal", Money(receipt.UntaxedAmount))}}
                {{Pair("Impuestos", Money(receipt.TaxAmount))}}
                <div class="pair total"><span>Total</span><strong>{{Money(receipt.PayableAmount)}}</strong></div>
                <hr class="rule">
                {{payments}}
                <hr class="rule">
                <div class="cufe"><strong>CUFE</strong><br>{{Encode(receipt.Cufe)}}</div>
                <div class="qr">{{qrSvg}}</div>
                <p class="center muted">Representación gráfica</p>
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

    private static string PaymentName(string code) => code switch
    {
        "Cash" => "Efectivo",
        "DebitCard" => "Tarjeta débito",
        "CreditCard" => "Tarjeta crédito",
        "Transfer" => "Transferencia",
        _ => code
    };

    private static string Money(decimal value) =>
        value.ToString("C0", ColombianCulture);

    private static string Quantity(decimal value) =>
        value.ToString("0.###", ColombianCulture);

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
