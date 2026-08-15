using System.Runtime.InteropServices;
using System.Text.Json;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public static class PosPrinterModes
{
    public const string BrowserPreview = "BrowserPreview";
    public const string WindowsRaw = "WindowsRaw";
    public const string File = "File";

    public static bool IsValid(string value) =>
        value is BrowserPreview or WindowsRaw or File;
}

public sealed record PosPrinterConfiguration(
    string ReceiptMode,
    string? ReceiptPrinterName,
    int ReceiptPaperWidthMillimeters,
    string? LetterPrinterName)
{
    public static PosPrinterConfiguration Default { get; } =
        new(PosPrinterModes.BrowserPreview, null, 80, null);
}

public sealed record PosPrinterConfigurationView(
    PosPrinterConfiguration Configuration,
    IReadOnlyList<string> InstalledPrinters);

public sealed class PosPrinterConfigurationStore(
    string settingsPath,
    string receiptOutputDirectory)
{
    private readonly object gate = new();

    public string ReceiptOutputDirectory { get; } = receiptOutputDirectory;

    public PosPrinterConfiguration Load()
    {
        lock (gate)
        {
            if (!File.Exists(settingsPath)) return PosPrinterConfiguration.Default;
            try
            {
                return JsonSerializer.Deserialize<PosPrinterConfiguration>(
                           File.ReadAllText(settingsPath))
                       ?? PosPrinterConfiguration.Default;
            }
            catch (JsonException)
            {
                return PosPrinterConfiguration.Default;
            }
        }
    }

    public PosPrinterConfiguration Save(PosPrinterConfiguration requested)
    {
        var mode = requested.ReceiptMode?.Trim() ?? string.Empty;
        if (!PosPrinterModes.IsValid(mode))
            throw new ArgumentException("El modo de impresion de tirilla no es valido.");
        if (requested.ReceiptPaperWidthMillimeters is not (58 or 80))
            throw new ArgumentException("La tirilla debe ser de 58 u 80 mm.");
        var receipt = Clean(requested.ReceiptPrinterName);
        var letter = Clean(requested.LetterPrinterName);
        if (mode == PosPrinterModes.WindowsRaw && receipt is null)
            throw new ArgumentException("Selecciona la impresora de tirilla.");

        var value = new PosPrinterConfiguration(
            mode, receipt, requested.ReceiptPaperWidthMillimeters, letter);
        lock (gate)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = settingsPath + ".new";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, settingsPath, overwrite: true);
        }
        return value;
    }

    public IReadOnlyList<string> InstalledPrinters() =>
        WindowsPrinterDiscovery.GetInstalledPrinters();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ConfigurablePosReceiptPrinter(
    PosPrinterConfigurationStore settings,
    EscPosReceiptRenderer escPos,
    HtmlReceiptPreviewRenderer html,
    IReceiptPreviewLauncher preview) : IPosReceiptPrinter
{
    public Task PrintAsync(
        PosReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        var configuration = settings.Load();
        var printer = configuration.ReceiptMode switch
        {
            PosPrinterModes.BrowserPreview => (IPosReceiptPrinter)
                new HtmlReceiptPreviewPrinter(
                    settings.ReceiptOutputDirectory, html, preview),
            PosPrinterModes.File => new FileReceiptPrinter(
                settings.ReceiptOutputDirectory, escPos),
            PosPrinterModes.WindowsRaw => new WindowsRawReceiptPrinter(
                configuration.ReceiptPrinterName
                    ?? throw new InvalidOperationException(
                        "La impresora de tirilla no esta configurada."),
                escPos),
            _ => throw new InvalidOperationException(
                "La configuracion de impresora no es valida.")
        };
        return printer.PrintAsync(
            receipt with
            {
                PaperWidthMillimeters =
                    configuration.ReceiptPaperWidthMillimeters
            },
            cancellationToken);
    }
}

internal static class WindowsPrinterDiscovery
{
    private const uint PrinterEnumLocal = 2;
    private const uint PrinterEnumConnections = 4;

    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var flags = PrinterEnumLocal | PrinterEnumConnections;
        EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var required, out _);
        if (required == 0) return [];
        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (!EnumPrinters(
                    flags, null, 4, buffer, required, out _, out var returned))
                return [];
            var size = Marshal.SizeOf<PrinterInfo4>();
            var names = new List<string>((int)returned);
            for (var index = 0; index < returned; index++)
            {
                var item = Marshal.PtrToStructure<PrinterInfo4>(
                    IntPtr.Add(buffer, checked((int)(index * size))));
                var name = Marshal.PtrToStringUni(item.PrinterName);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW",
        SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr buffer,
        uint bufferSize,
        out uint bytesNeeded,
        out uint printersReturned);
}
