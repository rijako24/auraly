using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing.Printing;
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
        Application.Run(form);
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
            ClientSize = new Size(ViewportWidth(command.PaperWidthMillimeters), 1200);
            await Task.Yield();
            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auraly", "PosEdge", "print-webview2");
            Directory.CreateDirectory(profile);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: profile);
            await browser.EnsureCoreWebView2Async(environment);
            var navigated = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            browser.CoreWebView2.NavigationCompleted += (_, eventArgs) =>
                navigated.TrySetResult(eventArgs.IsSuccess);
            browser.CoreWebView2.Navigate(new Uri(command.HtmlPath).AbsoluteUri);
            if (!await navigated.Task)
                throw new InvalidOperationException(
                    "No fue posible preparar el documento para impresión.");
            await Task.Delay(250);
            var heightJson = await browser.CoreWebView2.ExecuteScriptAsync(
                "Math.min(8000, Math.max(900, document.documentElement.scrollHeight))");
            var height = JsonSerializer.Deserialize<int>(heightJson);
            ClientSize = new Size(
                ViewportWidth(command.PaperWidthMillimeters),
                CssPixelsToDevicePixels(height));
            await Task.Delay(100);
            await using var png = new MemoryStream();
            await browser.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png, png);
            png.Position = 0;
            using var captured = new Bitmap(png);
            using var image = new Bitmap(captured);
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
                var width = ToHundredthsOfInch(paperWidth);
                var pageHeight = Math.Max(width,
                    (int)Math.Ceiling(width * image.Height / (double)image.Width));
                document.DefaultPageSettings.PaperSize = new PaperSize(
                    $"Auraly {paperWidth} mm", width, pageHeight);
            }
            document.PrintPage += (_, eventArgs) =>
            {
                var bounds = eventArgs.MarginBounds;
                var scale = Math.Min(
                    bounds.Width / (float)image.Width,
                    bounds.Height / (float)image.Height);
                var width = (int)Math.Round(image.Width * scale);
                var heightOnPage = (int)Math.Round(image.Height * scale);
                var target = new Rectangle(
                    bounds.Left + (bounds.Width - width) / 2,
                    bounds.Top,
                    width,
                    heightOnPage);
                var graphics = eventArgs.Graphics ?? throw new InvalidOperationException(
                    "Windows no entregó una superficie válida para imprimir.");
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
