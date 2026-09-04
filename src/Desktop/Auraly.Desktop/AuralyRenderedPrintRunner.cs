using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Auraly.Desktop;

internal sealed record AuralyRenderedPrintCommand(
    string HtmlPath,
    string PrinterName,
    int? PaperWidthMillimeters)
{
    public static bool TryParse(string[] args, out AuralyRenderedPrintCommand command)
    {
        command = null!;
        if (args.Length is not (4 or 6) ||
            !string.Equals(args[0], "--print-html", StringComparison.Ordinal) ||
            !string.Equals(args[2], "--printer", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(args[1]) ||
            string.IsNullOrWhiteSpace(args[3]))
            return false;
        int? paperWidthMillimeters = null;
        if (args.Length == 6 &&
            (!string.Equals(args[4], "--paper-width-mm", StringComparison.Ordinal) ||
             !int.TryParse(args[5], out var parsedWidth) ||
             parsedWidth is not (58 or 80)))
            return false;
        else if (args.Length == 6)
            paperWidthMillimeters = int.Parse(args[5]);
        var htmlPath = Path.GetFullPath(args[1]);
        if (!File.Exists(htmlPath)) return false;
        command = new AuralyRenderedPrintCommand(
            htmlPath, args[3].Trim(), paperWidthMillimeters);
        return true;
    }
}

internal static class AuralyRenderedPrintRunner
{
    public static int Run(AuralyRenderedPrintCommand command)
    {
        using var form = new AuralyRenderedPrintForm(command);
        System.Windows.Forms.Application.Run(form);
        return form.Handled ? 0 : 1;
    }
}

internal sealed class AuralyRenderedPrintForm : Form
{
    private readonly AuralyRenderedPrintCommand command;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };

    public bool Handled { get; private set; }

    public AuralyRenderedPrintForm(AuralyRenderedPrintCommand command)
    {
        this.command = command;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Opacity = 0.01;
        TopMost = true;
        ClientSize = new Size(800, 1200);
        Controls.Add(browser);
        Shown += PrintAsync;
    }

    private async void PrintAsync(object? sender, EventArgs args)
    {
        try
        {
            var installedPrinter = new PrinterSettings
            {
                PrinterName = command.PrinterName
            };
            if (!installedPrinter.IsValid)
                throw new InvalidOperationException(
                    $"La impresora configurada '{command.PrinterName}' ya no está disponible.");
            var output = SelectVirtualPrinterOutput(command.PrinterName);
            if (output.Cancelled)
            {
                Handled = true;
                return;
            }
            var isThermal = command.PaperWidthMillimeters is not null;
            ClientSize = new Size(
                ViewportWidth(command.PaperWidthMillimeters),
                1200);
            await Task.Yield();
            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auraly", "PosEdge", "print-webview2");
            Directory.CreateDirectory(profile);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: profile);
            await browser.EnsureCoreWebView2Async(environment);
            browser.ZoomFactor = 1;
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.print = () => undefined;");
            var navigated = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            browser.CoreWebView2.NavigationCompleted += (_, eventArgs) =>
                navigated.TrySetResult(eventArgs.IsSuccess);
            browser.CoreWebView2.Navigate(new Uri(command.HtmlPath).AbsoluteUri);
            if (!await navigated.Task)
                throw new InvalidOperationException(
                    "No fue posible preparar el documento para impresión.");
            await Task.Delay(250);
            await browser.CoreWebView2.ExecuteScriptAsync(
                """
                (() => {
                  const previewActions = document.querySelectorAll('.actions');
                  previewActions.forEach(element => element.remove());
                  [...document.querySelectorAll('p')]
                    .filter(element => element.textContent?.trim() === 'Representación gráfica')
                    .forEach(element => element.remove());
                  const receipt = document.querySelector('.receipt');
                  if (receipt) receipt.style.padding = '5mm 3mm 2mm 2mm';
                  const reportCode = document.documentElement.dataset.auralyReport;
                  const legacySalesReceipt = [...document.querySelectorAll('.receipt header .title')]
                    .some(element => element.textContent?.trim().toLowerCase() === 'comprobante de venta');
                  document.body.style.fontSize = reportCode === 'sales-receipt' || legacySalesReceipt
                    ? '11px'
                    : '12px';
                  document.body.style.lineHeight = '1.35';
                  const brand = document.querySelector('.brand');
                  if (brand) brand.style.fontSize = '23px';
                  document.querySelectorAll('.title').forEach(element => element.style.fontSize = '13px');
                  document.querySelectorAll('.ticket-number').forEach(element => element.style.fontSize = '12px');
                  const total = document.querySelector('.total');
                  if (total) {
                    total.style.fontSize = '16px';
                    const previous = total.previousElementSibling;
                    if (!previous?.classList.contains('rule')) {
                      const rule = document.createElement('hr');
                      rule.className = 'rule summary';
                      total.before(rule);
                    }
                    const next = total.nextElementSibling;
                    if (next?.classList.contains('rule')) next.classList.add('summary');
                  }
                  const cufe = document.querySelector('.cufe');
                  if (cufe) cufe.style.fontSize = '9px';
                  const qr = document.querySelector('.qr');
                  if (qr) {
                    qr.style.width = '38mm';
                    qr.style.marginTop = '6px';
                    qr.style.marginBottom = '4px';
                  }
                  document.querySelectorAll('.rule').forEach(element => {
                    element.style.marginTop = '8px';
                    element.style.marginBottom = '8px';
                  });
                  const firstTitle = document.querySelector('.receipt header .title');
                  const titleElements = [...document.querySelectorAll('.receipt header .title')];
                  if (firstTitle && ['factura electronica de venta', 'factura electrónica de venta', 'número de ticket'].includes(firstTitle.textContent?.trim().toLowerCase())) {
                    const number = titleElements[1]?.textContent?.trim() || firstTitle.textContent?.replace(/^n(?:ú|u)mero de ticket:?\s*/i, '').trim();
                    if (number) {
                      firstTitle.textContent = 'N.º de ticket: ';
                      const strong = document.createElement('strong');
                      strong.textContent = number.replace(/^N\.º\s*/i, '');
                      firstTitle.append(strong);
                      firstTitle.classList.add('ticket-number');
                      if (titleElements[1]) titleElements[1].remove();
                    }
                  } else if (firstTitle?.textContent?.trim().toLowerCase() === 'comprobante de venta' && titleElements[1]) {
                    const number = titleElements[1].textContent?.trim().replace(/^N\.º\s*/i, '');
                    titleElements[1].textContent = 'N.º de ticket: ';
                    const strong = document.createElement('strong');
                    strong.textContent = number;
                    titleElements[1].append(strong);
                    titleElements[1].classList.add('ticket-number');
                  }
                  const scope = document.querySelector('.receipt header .scope');
                  if (scope) scope.textContent = scope.textContent?.split(/\s+(?:-|·)\s+/)[0] ?? '';
                  document.querySelectorAll('.receipt header > * + *')
                    .forEach(element => element.style.marginTop = '4px');
                  const taxTitle = [...document.querySelectorAll('.section-title')]
                    .find(element => element.textContent?.trim() === 'Impuestos por tarifa');
                  if (taxTitle) {
                    const subtotal = taxTitle.previousElementSibling;
                    const table = taxTitle.nextElementSibling;
                    if (subtotal?.classList.contains('pair') && table?.classList.contains('tax-table'))
                      table.after(subtotal);
                  }
                  const totalTaxes = [...document.querySelectorAll('.pair')]
                    .find(element => element.firstElementChild?.textContent?.trim() === 'Total impuestos');
                  if (totalTaxes && !totalTaxes.nextElementSibling?.classList.contains('rule')) {
                    const rule = document.createElement('hr');
                    rule.className = 'rule summary';
                    totalTaxes.after(rule);
                  } else if (totalTaxes?.nextElementSibling?.classList.contains('rule')) {
                    totalTaxes.nextElementSibling.classList.add('summary');
                  }
                  document.documentElement.style.background = 'white';
                  document.documentElement.style.overflow = 'hidden';
                  document.body.style.margin = '0';
                  document.body.style.boxShadow = 'none';
                })()
                """);
            var thermalRasterWidth = command.PaperWidthMillimeters is int thermalPaperWidth
                ? ThermalRasterWidth(installedPrinter, thermalPaperWidth)
                : 0;
            QrRasterGeometry? qrGeometry = null;
            await using var png = new MemoryStream();
            if (isThermal)
            {
                var widthJson = await browser.CoreWebView2.ExecuteScriptAsync(
                    "Math.max(document.documentElement.scrollWidth, document.body.scrollWidth)");
                var width = JsonSerializer.Deserialize<int>(widthJson);
                var captureScale = thermalRasterWidth / (double)width;
                await browser.CoreWebView2.ExecuteScriptAsync(
                    $$"""
                    (() => {
                      const qr = document.querySelector('.qr');
                      const svg = qr?.querySelector('svg');
                      const modules = svg?.viewBox?.baseVal?.width;
                      if (qr && modules)
                        qr.style.width = `${modules * 4 / {{captureScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}px`;
                    })()
                    """);
                var qrGeometryJson = await browser.CoreWebView2.ExecuteScriptAsync(
                    "(() => { const qr=document.querySelector('.qr'); const svg=qr?.querySelector('svg'); if(!qr||!svg) return null; const r=qr.getBoundingClientRect(); return [r.left,r.top,svg.viewBox.baseVal.width]; })()");
                var qrValues = JsonSerializer.Deserialize<double[]?>(qrGeometryJson);
                if (qrValues is { Length: 3 })
                    qrGeometry = new QrRasterGeometry(
                        (int)Math.Round(qrValues[0] * captureScale),
                        (int)Math.Round(qrValues[1] * captureScale),
                        (int)Math.Round(qrValues[2]),
                        4);
                var heightJson = await browser.CoreWebView2.ExecuteScriptAsync(
                    "Math.min(16000, Math.max(1, Math.ceil(document.querySelector('.receipt')?.getBoundingClientRect().bottom ?? document.body.scrollHeight)))");
                var height = JsonSerializer.Deserialize<int>(heightJson);
                var response = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Page.captureScreenshot",
                    JsonSerializer.Serialize(new
                    {
                        format = "png",
                        fromSurface = true,
                        captureBeyondViewport = true,
                        clip = new { x = 0, y = 0, width, height, scale = captureScale }
                    }));
                using var payload = JsonDocument.Parse(response);
                var data = Convert.FromBase64String(
                    payload.RootElement.GetProperty("data").GetString()
                    ?? throw new InvalidOperationException(
                        "El navegador no generó la imagen completa de la tirilla."));
                await png.WriteAsync(data);
            }
            else
            {
                var heightJson = await browser.CoreWebView2.ExecuteScriptAsync(
                    "Math.min(16000, Math.max(1, document.documentElement.scrollHeight, document.body.scrollHeight))");
                var height = JsonSerializer.Deserialize<int>(heightJson);
                ClientSize = new Size(800, CssPixelsToDevicePixels(height));
                await Task.Delay(100);
                await browser.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, png);
            }
            png.Position = 0;
            using var captured = new Bitmap(png);
            using var image = command.PaperWidthMillimeters is null
                ? new Bitmap(captured)
                : ToThermalMonochrome(
                    captured,
                    thermalRasterWidth,
                    qrGeometry);
            var diagnosticPreview = Environment.GetEnvironmentVariable(
                "AURALY_PRINT_DIAGNOSTIC_PNG");
            if (!string.IsNullOrWhiteSpace(diagnosticPreview))
            {
                image.Save(Path.GetFullPath(diagnosticPreview), ImageFormat.Png);
                Handled = true;
                return;
            }
            if (output.Path is null && command.PaperWidthMillimeters is not null)
            {
                WindowsRawPrintJob.Print(
                    command.PrinterName,
                    Path.GetFileNameWithoutExtension(command.HtmlPath),
                    EncodeEscPosRaster(image));
                Handled = true;
                return;
            }
            using var document = new PrintDocument
            {
                DocumentName = Path.GetFileNameWithoutExtension(command.HtmlPath),
                PrintController = new StandardPrintController()
            };
            document.PrinterSettings.PrinterName = command.PrinterName;
            if (output.Path is not null)
            {
                document.PrinterSettings.PrintToFile = true;
                document.PrinterSettings.PrintFileName = output.Path;
            }
            document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            if (command.PaperWidthMillimeters is int paperWidth)
            {
                var configuredWidth = ToHundredthsOfInch(paperWidth);
                var driverWidth = installedPrinter.DefaultPageSettings.PaperSize.Width;
                var width = output.Path is null && driverWidth > 0
                    ? Math.Min(configuredWidth, driverWidth)
                    : configuredWidth;
                var pageHeight = Math.Max(width,
                    (int)Math.Ceiling(width * image.Height / (double)image.Width));
                document.DefaultPageSettings.PaperSize = new PaperSize(
                    $"Auraly {paperWidth} mm", width, pageHeight);
            }
            document.PrintPage += (_, eventArgs) =>
            {
                var bounds = command.PaperWidthMillimeters is null
                    ? eventArgs.MarginBounds
                    : Rectangle.Round(eventArgs.PageSettings.PrintableArea);
                var usableWidth = command.PaperWidthMillimeters is null
                    ? bounds.Width
                    : (int)Math.Floor(bounds.Width * 0.96d);
                var scale = Math.Min(
                    usableWidth / (float)image.Width,
                    bounds.Height / (float)image.Height);
                var width = (int)Math.Round(image.Width * scale);
                var heightOnPage = (int)Math.Round(image.Height * scale);
                var target = new Rectangle(
                    command.PaperWidthMillimeters is null
                        ? bounds.Left + (bounds.Width - width) / 2
                        : bounds.Left,
                    bounds.Top,
                    width,
                    heightOnPage);
                var graphics = eventArgs.Graphics ?? throw new InvalidOperationException(
                    "Windows no entregó una superficie válida para imprimir.");
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.DrawImage(image, target);
                eventArgs.HasMorePages = false;
            };
            document.Print();
            Handled = true;
        }
        catch (Exception error)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auraly", "PosEdge", "logs");
            Directory.CreateDirectory(logDirectory);
            await File.AppendAllTextAsync(
                Path.Combine(logDirectory, "print-error.log"),
                $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}");
        }
        finally
        {
            Close();
        }
    }

    private static Bitmap ToThermalMonochrome(
        Bitmap source,
        int targetWidth,
        QrRasterGeometry? qrGeometry)
    {
        var targetHeight = (int)Math.Ceiling(
            source.Height * targetWidth / (double)source.Width);
        var monochrome = new Bitmap(
            targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(monochrome))
        {
            graphics.Clear(Color.White);
            if (source.Width == targetWidth && source.Height == targetHeight)
            {
                graphics.DrawImageUnscaled(source, 0, 0);
            }
            else
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, targetWidth, targetHeight),
                    new Rectangle(0, 0, source.Width, source.Height),
                    GraphicsUnit.Pixel);
            }
        }

        var bounds = new Rectangle(0, 0, monochrome.Width, monochrome.Height);
        var data = monochrome.LockBits(
            bounds, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var index = 0; index < bytes.Length; index += 3)
            {
                var luminance =
                    (bytes[index] * 29 + bytes[index + 1] * 150 + bytes[index + 2] * 77) >> 8;
                var value = luminance < 220 ? (byte)0 : (byte)255;
                bytes[index] = value;
                bytes[index + 1] = value;
                bytes[index + 2] = value;
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            monochrome.UnlockBits(data);
        }
        if (qrGeometry is not null)
            SnapQrModules(monochrome, qrGeometry);
        return monochrome;
    }

    private static void SnapQrModules(Bitmap image, QrRasterGeometry geometry)
    {
        using var graphics = Graphics.FromImage(image);
        for (var moduleY = 0; moduleY < geometry.Modules; moduleY++)
        {
            for (var moduleX = 0; moduleX < geometry.Modules; moduleX++)
            {
                var x = geometry.Left + moduleX * geometry.PixelsPerModule;
                var y = geometry.Top + moduleY * geometry.PixelsPerModule;
                var sampleX = Math.Clamp(
                    x + geometry.PixelsPerModule / 2, 0, image.Width - 1);
                var sampleY = Math.Clamp(
                    y + geometry.PixelsPerModule / 2, 0, image.Height - 1);
                graphics.FillRectangle(
                    image.GetPixel(sampleX, sampleY).R < 128
                        ? Brushes.Black
                        : Brushes.White,
                    x,
                    y,
                    geometry.PixelsPerModule,
                    geometry.PixelsPerModule);
            }
        }
    }

    private sealed record QrRasterGeometry(
        int Left,
        int Top,
        int Modules,
        int PixelsPerModule);

    private static int ThermalRasterWidth(
        PrinterSettings printer,
        int paperWidthMillimeters)
    {
        var page = printer.DefaultPageSettings;
        var dpi = page.PrinterResolution.X > 0
            ? page.PrinterResolution.X
            : 203;
        var paperDots = (int)Math.Round(paperWidthMillimeters * dpi / 25.4d);
        var printableDots = page.PrintableArea.Width > 0
            ? (int)Math.Round(page.PrintableArea.Width * dpi / 100d)
            : paperDots;
        var measured = Math.Min(paperDots, printableDots);
        if (measured is < 256 or > 1024)
            measured = paperWidthMillimeters == 58 ? 384 : 576;
        return Math.Max(256, measured / 8 * 8);
    }

    private static byte[] EncodeEscPosRaster(Bitmap image)
    {
        const int rowsPerCommand = 128;
        var rightGuardPixels = Math.Max(8, image.Width / 36);
        var lastInkRow = 0;
        for (var y = image.Height - 1; y >= 0; y--)
        {
            var hasInk = false;
            for (var x = 0; x < image.Width && !hasInk; x++)
                hasInk = image.GetPixel(x, y).R < 128;
            if (!hasInk) continue;
            lastInkRow = y;
            break;
        }
        var height = Math.Min(image.Height, lastInkRow + 9);
        var bytesPerRow = (image.Width + 7) / 8;
        using var output = new MemoryStream();
        output.Write([0x1B, 0x40, 0x1B, 0x61, 0x01]);
        for (var top = 0; top < height; top += rowsPerCommand)
        {
            var rows = Math.Min(rowsPerCommand, height - top);
            output.Write([0x1D, 0x76, 0x30, 0x00]);
            output.WriteByte((byte)(bytesPerRow & 0xFF));
            output.WriteByte((byte)(bytesPerRow >> 8));
            output.WriteByte((byte)(rows & 0xFF));
            output.WriteByte((byte)(rows >> 8));
            for (var y = top; y < top + rows; y++)
            {
                for (var byteIndex = 0; byteIndex < bytesPerRow; byteIndex++)
                {
                    byte value = 0;
                    for (var bit = 0; bit < 8; bit++)
                    {
                        var x = byteIndex * 8 + bit;
                        if (x < image.Width - rightGuardPixels &&
                            image.GetPixel(x, y).R < 128)
                            value |= (byte)(0x80 >> bit);
                    }
                    output.WriteByte(value);
                }
            }
        }
        // Feed the printed content beyond the cutter before issuing the cut.
        // Four standard text lines cover the printhead-to-cutter gap used by
        // common Epson, Xprinter and ESC/POS-compatible thermal printers.
        output.Write([0x1B, 0x64, 0x04, 0x1D, 0x56, 0x41, 0x03]);
        return output.ToArray();
    }

    private VirtualPrinterOutput SelectVirtualPrinterOutput(string printerName)
    {
        string? extension = null;
        string? filter = null;
        if (printerName.Contains("XPS", StringComparison.OrdinalIgnoreCase))
        {
            extension = "xps";
            filter = "Documento XPS (*.xps)|*.xps";
        }
        else if (printerName.Contains("PDF", StringComparison.OrdinalIgnoreCase))
        {
            extension = "pdf";
            filter = "Documento PDF (*.pdf)|*.pdf";
        }
        if (extension is null) return new VirtualPrinterOutput(null, false);

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = extension,
            FileName = Path.GetFileNameWithoutExtension(command.HtmlPath),
            Filter = filter!,
            OverwritePrompt = true,
            RestoreDirectory = true,
            Title = "Guardar comprobante impreso"
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? new VirtualPrinterOutput(dialog.FileName, false)
            : new VirtualPrinterOutput(null, true);
    }

    private sealed record VirtualPrinterOutput(string? Path, bool Cancelled);

    private int ViewportWidth(int? paperWidthMillimeters) =>
        paperWidthMillimeters is int width
            ? Math.Max(220, (int)Math.Ceiling(width * DeviceDpi / 25.4d))
            : 800;

    private int CssPixelsToDevicePixels(int cssPixels) =>
        (int)Math.Ceiling(cssPixels * DeviceDpi / 96d);

    private static int ToHundredthsOfInch(int millimeters) =>
        (int)Math.Round(millimeters * 100d / 25.4d);

    protected override void Dispose(bool disposing)
    {
        if (disposing) browser.Dispose();
        base.Dispose(disposing);
    }
}
