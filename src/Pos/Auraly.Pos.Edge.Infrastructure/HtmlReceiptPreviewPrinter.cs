using System.Diagnostics;
using System.Text;
using Auraly.Pos.Printing;

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
    public string Render(PosReceipt receipt, int? templateVersion = null, bool autoPrint = true) =>
        new SalesReceiptHtmlRenderer().Render(receipt.ToPrintDocument(),
            receipt.PaperWidthMillimeters, receipt.BusinessName, templateVersion, autoPrint);
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
