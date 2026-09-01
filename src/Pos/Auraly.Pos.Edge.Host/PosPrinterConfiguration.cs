using System.Runtime.InteropServices;
using System.IO.Ports;
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

public static class OrderPrinterModes
{
    public const string BrowserPreview = "BrowserPreview";
    public const string WindowsPrint = "WindowsPrint";

    public static bool IsValid(string value) =>
        value is BrowserPreview or WindowsPrint;
}

public static class PrintTemplateFormats
{
    public const string Receipt = "Receipt";
    public const string HalfLetter = "HalfLetter";
    public const string HalfLegal = "HalfLegal";
    public const string Letter = "Letter";
}

public static class WindowsPrinterOutput
{
    public static bool RequiresRenderedDocument(string? printerName) =>
        !string.IsNullOrWhiteSpace(printerName) &&
        (printerName.Contains("PDF", StringComparison.OrdinalIgnoreCase) ||
         printerName.Contains("XPS", StringComparison.OrdinalIgnoreCase));
}

public interface IWindowsRenderedPrintJob
{
    Task PrintAsync(
        string printerName,
        string documentName,
        string html,
        string outputDirectory,
        int? paperWidthMillimeters,
        CancellationToken cancellationToken);
}

public sealed class SystemWindowsRenderedPrintJob : IWindowsRenderedPrintJob
{
    public async Task PrintAsync(
        string printerName,
        string documentName,
        string html,
        string outputDirectory,
        int? paperWidthMillimeters,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "La impresión local renderizada requiere Windows.");
        Directory.CreateDirectory(outputDirectory);
        var target = Path.Combine(
            outputDirectory,
            $"{string.Concat(documentName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character))}-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(
            target, html, new System.Text.UTF8Encoding(false), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var desktop = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "Auraly.Desktop.exe"));
        if (!File.Exists(desktop))
            throw new InvalidOperationException(
                "No se encontró el adaptador local de impresión de Auraly.");
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(desktop)
            {
                WorkingDirectory = Path.GetDirectoryName(desktop)!,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("--print-html");
        process.StartInfo.ArgumentList.Add(Path.GetFullPath(target));
        process.StartInfo.ArgumentList.Add("--printer");
        process.StartInfo.ArgumentList.Add(printerName);
        if (paperWidthMillimeters is int paperWidth)
        {
            process.StartInfo.ArgumentList.Add("--paper-width-mm");
            process.StartInfo.ArgumentList.Add(paperWidth.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!process.Start())
            throw new IOException("No fue posible iniciar la impresión local.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new IOException(
                $"La impresora '{printerName}' no pudo completar el trabajo.");
    }
}

public sealed record PrintTemplateRoute(
    string DocumentType,
    string Format,
    string? PrinterName);

public sealed record PosPrinterConfiguration(
    string ReceiptMode,
    string? ReceiptPrinterName,
    int ReceiptPaperWidthMillimeters,
    string? LetterPrinterName,
    string OrderMode = OrderPrinterModes.WindowsPrint,
    IReadOnlyList<PrintTemplateRoute>? TemplateRoutes = null,
    string PosOutputFormat = PrintTemplateFormats.Receipt,
    string OrdersOutputFormat = PrintTemplateFormats.HalfLetter,
    PosScaleConfiguration? Scale = null,
    string? PosPrinterName = null,
    string? OrdersPrinterName = null,
    int OrdersReceiptPaperWidthMillimeters = 80)
{
    public static PosPrinterConfiguration Default { get; } =
        new(PosPrinterModes.WindowsRaw, null, 80, null,
            OrderPrinterModes.WindowsPrint);
}

public sealed record PosScaleConfiguration(
    bool Enabled,
    string PortName,
    int BaudRate = 9600,
    int DataBits = 8,
    string Parity = "None",
    string StopBits = "One",
    bool SendsRequest = false,
    string RequestText = "",
    int StartIndex = 0,
    int Length = 0,
    bool Reverse = false,
    bool DivideBy1000 = false,
    int TimeoutMilliseconds = 2000);

public sealed record PosPrinterConfigurationView(
    PosPrinterConfiguration Configuration,
    IReadOnlyList<string> InstalledPrinters,
    IReadOnlyList<string> SerialPorts);

public sealed class PosPrinterConfigurationStore(
    string settingsPath,
    string receiptOutputDirectory,
    PosPrinterConfiguration? enrollmentDefault = null)
{
    private readonly object gate = new();
    private readonly PosPrinterConfiguration initial =
        enrollmentDefault ?? PosPrinterConfiguration.Default;

    public string ReceiptOutputDirectory { get; } = receiptOutputDirectory;

    public PosPrinterConfiguration Load()
    {
        lock (gate)
        {
            if (!File.Exists(settingsPath)) return initial;
            try
            {
                var stored = JsonSerializer.Deserialize<PosPrinterConfiguration>(
                                 File.ReadAllText(settingsPath))
                             ?? PosPrinterConfiguration.Default;
                return stored with
                {
                    ReceiptMode = PosPrinterModes.WindowsRaw,
                    OrderMode = OrderPrinterModes.WindowsPrint
                };
            }
            catch (JsonException)
            {
                return initial;
            }
        }
    }

    public PosPrinterConfiguration Save(PosPrinterConfiguration requested)
    {
        var mode = requested.ReceiptMode?.Trim() ?? string.Empty;
        if (!PosPrinterModes.IsValid(mode))
            throw new ArgumentException("El modo de impresion de tirilla no es valido.");
        if (mode == PosPrinterModes.BrowserPreview)
            throw new ArgumentException("La caja debe imprimir la tirilla directamente.");
        if (requested.ReceiptPaperWidthMillimeters is not (58 or 80))
            throw new ArgumentException("La tirilla debe ser de 58 u 80 mm.");
        if (requested.OrdersReceiptPaperWidthMillimeters is not (58 or 80))
            throw new ArgumentException("La tirilla de pedidos debe ser de 58 u 80 mm.");
        var receipt = Clean(requested.ReceiptPrinterName);
        var letter = Clean(requested.LetterPrinterName);
        var orderMode = requested.OrderMode?.Trim() ?? string.Empty;
        if (!OrderPrinterModes.IsValid(orderMode))
            throw new ArgumentException("El modo de impresion de pedidos no es valido.");
        if (orderMode == OrderPrinterModes.BrowserPreview)
            throw new ArgumentException("La caja debe imprimir los pedidos directamente.");
        if (!IsWorkflowFormat(requested.PosOutputFormat) ||
            !IsWorkflowFormat(requested.OrdersOutputFormat))
            throw new ArgumentException(
                "El formato debe ser tirilla, media carta, media oficio o carta.");
        var routes = NormalizeRoutes(requested.TemplateRoutes, receipt, letter);
        var scale = ValidateScale(requested.Scale);
        var posPrinter = Clean(requested.PosPrinterName) ?? PrinterForFormat(routes, requested.PosOutputFormat);
        var ordersPrinter = Clean(requested.OrdersPrinterName) ?? PrinterForFormat(routes, requested.OrdersOutputFormat);
        if (posPrinter is null) throw new ArgumentException("Selecciona la impresora del punto de venta.");
        if (ordersPrinter is null) throw new ArgumentException("Selecciona la impresora de pedidos.");

        var value = new PosPrinterConfiguration(
            mode, receipt, requested.ReceiptPaperWidthMillimeters, letter,
            orderMode, routes,
            requested.PosOutputFormat, requested.OrdersOutputFormat, scale,
            posPrinter, ordersPrinter, requested.OrdersReceiptPaperWidthMillimeters);
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

    public IReadOnlyList<string> SerialPorts() =>
        SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static PosScaleConfiguration? ValidateScale(PosScaleConfiguration? scale)
    {
        if (scale is null || !scale.Enabled) return scale;
        if (string.IsNullOrWhiteSpace(scale.PortName))
            throw new ArgumentException("Selecciona el puerto COM de la balanza.");
        if (scale.BaudRate <= 0 || scale.DataBits is < 5 or > 8)
            throw new ArgumentException("La comunicación de la balanza no es válida.");
        if (!Enum.TryParse<Parity>(scale.Parity, true, out _) ||
            !Enum.TryParse<StopBits>(scale.StopBits, true, out var stopBits) ||
            stopBits == StopBits.None)
            throw new ArgumentException("La paridad o los bits de parada de la balanza no son válidos.");
        if (scale.StartIndex < 0 || scale.Length < 0 ||
            scale.TimeoutMilliseconds is < 200 or > 10000)
            throw new ArgumentException("La regla de lectura de la balanza no es válida.");
        if (scale.SendsRequest && string.IsNullOrWhiteSpace(scale.RequestText))
            throw new ArgumentException("Escribe el comando de lectura que requiere la balanza.");
        return scale with { PortName = scale.PortName.Trim(), RequestText = scale.RequestText ?? string.Empty };
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsWorkflowFormat(string? value) =>
        value is PrintTemplateFormats.Receipt or
            PrintTemplateFormats.HalfLetter or
            PrintTemplateFormats.HalfLegal or
            PrintTemplateFormats.Letter;

    private static string? PrinterForFormat(
        IReadOnlyList<PrintTemplateRoute> routes, string format) =>
        routes.FirstOrDefault(route => route.Format == format)?.PrinterName;

    private static IReadOnlyList<PrintTemplateRoute> NormalizeRoutes(
        IReadOnlyList<PrintTemplateRoute>? requested,
        string? receiptFallback,
        string? letterFallback)
    {
        var routes = requested ?? [];
        return RequiredRoutes.Select(key => new PrintTemplateRoute(
            key.DocumentType,
            key.Format,
            Clean(routes.FirstOrDefault(route =>
                route.DocumentType == key.DocumentType &&
                route.Format == key.Format)?.PrinterName) ??
            (key.Format == PrintTemplateFormats.Receipt
                ? receiptFallback
                : letterFallback))).ToArray();
    }

    private static readonly (string DocumentType, string Format)[] RequiredRoutes =
    [
        ("SalesInvoice", PrintTemplateFormats.Receipt),
        ("SalesReceipt", PrintTemplateFormats.Receipt),
        ("SalesInvoice", PrintTemplateFormats.HalfLetter),
        ("SalesReceipt", PrintTemplateFormats.HalfLetter),
        ("SalesInvoice", PrintTemplateFormats.HalfLegal),
        ("SalesReceipt", PrintTemplateFormats.HalfLegal),
        ("SalesInvoice", PrintTemplateFormats.Letter),
        ("SalesReceipt", PrintTemplateFormats.Letter)
    ];
}

public static class PosPrinterConfigurationExtensions
{
    public static string? PrinterFor(
        this PosPrinterConfiguration configuration,
        string documentType,
        string format)
    {
        var route = configuration.TemplateRoutes?.FirstOrDefault(item =>
            item.DocumentType == documentType && item.Format == format);
        return route?.PrinterName ??
               (format == PrintTemplateFormats.Receipt
                   ? configuration.ReceiptPrinterName
                   : configuration.LetterPrinterName);
    }
}

public sealed class ConfigurableOrderDocumentPrinter(
    PosPrinterConfigurationStore settings,
    HalfLetterDocumentRenderer renderer,
    IWindowsRenderedPrintJob renderedPrintJob)
{
    public async Task PrintAsync(
        IReadOnlyCollection<Auraly.Contracts.Sales.OnlineSalesReceipt> receipts,
        string? workflowPrinterName = null,
        string? outputFormat = null,
        CancellationToken cancellationToken = default)
    {
        if (receipts.Count == 0) return;
        var configuration = settings.Load();
        var format = outputFormat ?? configuration.OrdersOutputFormat;
        if (format == PrintTemplateFormats.Receipt)
            throw new ArgumentException(
                "La impresora de documentos requiere un formato de hoja.",
                nameof(outputFormat));
        var directory = Path.Combine(
            settings.ReceiptOutputDirectory,
            format switch
            {
                PrintTemplateFormats.HalfLetter => "media-carta",
                PrintTemplateFormats.HalfLegal => "media-oficio",
                PrintTemplateFormats.Letter => "carta",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(outputFormat), "El formato de hoja no es válido.")
            });
        Directory.CreateDirectory(directory);
        var batches = configuration.OrderMode == OrderPrinterModes.WindowsPrint
            ? receipts.GroupBy(receipt => workflowPrinterName ?? configuration.PrinterFor(
                receipt.DocumentType, format))
            : receipts.GroupBy(_ => (string?)null);
        foreach (var batch in batches)
        {
            var rendered = renderer.Render(batch.ToArray(), format);
            if (configuration.OrderMode == OrderPrinterModes.WindowsPrint)
            {
                var printerName = batch.Key
                    ?? throw new InvalidOperationException(
                        "La impresora para este formato no está configurada.");
                await renderedPrintJob.PrintAsync(
                    printerName,
                    $"Auraly-{format}",
                    rendered,
                    directory,
                    null,
                    cancellationToken);
                continue;
            }

            var target = Path.Combine(directory, $"{format}-{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(
                target, rendered, new System.Text.UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(
                    "La vista previa local de documentos requiere Windows.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.GetFullPath(target),
                UseShellExecute = true
            });
        }
    }

    public Task PrintAsync(
        PosReceipt receipt,
        string? workflowPrinterName = null,
        string outputFormat = PrintTemplateFormats.HalfLetter,
        CancellationToken cancellationToken = default) =>
        PrintAsync(
        [
            new Auraly.Contracts.Sales.OnlineSalesReceipt(
                receipt.DocumentId.Value,
                receipt.DocumentType,
                receipt.DocumentNumber,
                receipt.FiscalNumber,
                receipt.IssuedAt,
                receipt.CustomerIdentification,
                receipt.Lines.Select(line =>
                    new Auraly.Contracts.Sales.OnlineSalesReceiptLine(
                        line.ProductCode, line.Description, line.Quantity,
                        line.UnitPrice, line.Discount, line.Tax, line.Total,
                        line.TaxCode, line.TaxRate)).ToArray(),
                receipt.Payments.Select(payment =>
                    new Auraly.Contracts.Sales.OnlineSalesPayment(
                        payment.MethodCode, payment.Amount, payment.Reference,
                        payment.CardFranchiseCode, payment.ApprovalNumber)).ToArray(),
                receipt.UntaxedAmount,
                receipt.TaxAmount,
                receipt.PayableAmount,
                receipt.Cufe,
                receipt.QrPayload,
                null,
                receipt.CustomerIdentification,
                receipt.CompanyName,
                receipt.CompanyLogoSource)
        ], workflowPrinterName, outputFormat, cancellationToken);
}

public sealed class ConfigurablePosReceiptPrinter(
    PosPrinterConfigurationStore settings,
    EscPosReceiptRenderer escPos,
    HtmlReceiptPreviewRenderer html,
    IReceiptPreviewLauncher preview,
    IWindowsRawPrintJob rawPrintJob,
    IWindowsRenderedPrintJob renderedPrintJob,
    ConfigurableOrderDocumentPrinter orderDocumentPrinter,
    PosWorkstationIdentity? workstation = null) : IPosReceiptPrinter
{
    public Task PrintAsync(
        PosReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        var configuration = settings.Load();
        if (configuration.PosOutputFormat != PrintTemplateFormats.Receipt)
            return orderDocumentPrinter.PrintAsync(
                receipt,
                configuration.PosPrinterName,
                configuration.PosOutputFormat,
                cancellationToken);
        return PrintReceiptAsync(receipt, cancellationToken);
    }

    public Task PrintReceiptAsync(
        PosReceipt receipt,
        CancellationToken cancellationToken = default) =>
        PrintReceiptAsync(receipt, settings.Load().PosPrinterName,
            settings.Load().ReceiptPaperWidthMillimeters, cancellationToken);

    private Task PrintReceiptAsync(
        PosReceipt receipt,
        string? workflowPrinterName,
        int paperWidthMillimeters,
        CancellationToken cancellationToken)
    {
        receipt = receipt with
        {
            CompanyName = string.IsNullOrWhiteSpace(receipt.CompanyName)
                ? workstation?.CompanyName
                : receipt.CompanyName,
            CompanyLogoSource = string.IsNullOrWhiteSpace(receipt.CompanyLogoSource)
                ? workstation?.CompanyLogoSource
                : receipt.CompanyLogoSource
        };
        var configuration = settings.Load();
        var printerName = workflowPrinterName ?? configuration.PrinterFor(
            receipt.DocumentType, PrintTemplateFormats.Receipt);
        if (configuration.ReceiptMode == PosPrinterModes.WindowsRaw &&
            WindowsPrinterOutput.RequiresRenderedDocument(printerName))
        {
            return renderedPrintJob.PrintAsync(
                printerName!,
                $"Auraly-{receipt.DocumentNumber}",
                html.Render(receipt with { PaperWidthMillimeters = paperWidthMillimeters }),
                settings.ReceiptOutputDirectory,
                paperWidthMillimeters,
                cancellationToken);
        }
        var printer = configuration.ReceiptMode switch
        {
            PosPrinterModes.BrowserPreview => (IPosReceiptPrinter)
                new HtmlReceiptPreviewPrinter(
                    settings.ReceiptOutputDirectory, html, preview),
            PosPrinterModes.File => new FileReceiptPrinter(
                settings.ReceiptOutputDirectory, escPos),
            PosPrinterModes.WindowsRaw => ReceiptPrinterForWindows(
                printerName
                    ?? throw new InvalidOperationException(
                        "La impresora de tirilla no esta configurada.")),
            _ => throw new InvalidOperationException(
                "La configuracion de impresora no es valida.")
        };
        return printer.PrintAsync(
            receipt with
            {
                PaperWidthMillimeters =
                    paperWidthMillimeters
            },
            cancellationToken);
    }

    private IPosReceiptPrinter ReceiptPrinterForWindows(string printerName)
    {
        return new WindowsRawReceiptPrinter(printerName, escPos, rawPrintJob);
    }

    public Task PrintReceiptAsync(
        Auraly.Contracts.Sales.OnlineSalesReceipt receipt,
        CancellationToken cancellationToken = default) =>
        PrintOnlineReceiptAsync(receipt, false, cancellationToken);

    public Task PrintOrdersReceiptAsync(
        Auraly.Contracts.Sales.OnlineSalesReceipt receipt,
        CancellationToken cancellationToken = default) =>
        PrintOnlineReceiptAsync(receipt, true, cancellationToken);

    private Task PrintOnlineReceiptAsync(
        Auraly.Contracts.Sales.OnlineSalesReceipt receipt,
        bool ordersWorkflow,
        CancellationToken cancellationToken)
    {
        var configuration = settings.Load();
        return PrintReceiptAsync(
            new PosReceipt(
                Guid.NewGuid(),
                new Auraly.BuildingBlocks.Domain.Identifiers.DocumentId(
                    receipt.DocumentId),
                receipt.DocumentNumber,
                receipt.FiscalNumber,
                receipt.IssuedAt,
                receipt.CustomerIdentification,
                receipt.Lines.Select(line => new PosReceiptLine(
                    line.ProductCode, line.Description, line.Quantity,
                    line.UnitPrice, line.Discount, line.Tax, line.Total,
                    line.TaxCode, line.TaxRate)).ToArray(),
                receipt.Payments.Select(payment => new OfflineSalePayment(
                    payment.MethodCode, payment.Amount, payment.Reference,
                    payment.CardFranchiseCode, payment.ApprovalNumber,
                    payment.BankAccountId, payment.Notes)).ToArray(),
                receipt.UntaxedAmount,
                receipt.TaxAmount,
                receipt.PayableAmount,
                receipt.Cufe,
                receipt.QrPayload,
                ordersWorkflow
                    ? configuration.OrdersReceiptPaperWidthMillimeters
                    : configuration.ReceiptPaperWidthMillimeters,
                receipt.DocumentType,
                receipt.CompanyName,
                receipt.CompanyLogoSource),
            ordersWorkflow ? configuration.OrdersPrinterName : configuration.PosPrinterName,
            ordersWorkflow
                ? configuration.OrdersReceiptPaperWidthMillimeters
                : configuration.ReceiptPaperWidthMillimeters,
            cancellationToken);
    }
}

public sealed class RenderedWindowsReceiptPrinter(
    string printerName,
    string outputDirectory,
    HtmlReceiptPreviewRenderer renderer,
    IWindowsRenderedPrintJob printJob) : IPosReceiptPrinter
{
    public Task PrintAsync(
        PosReceipt receipt,
        CancellationToken cancellationToken = default) =>
        printJob.PrintAsync(
            printerName,
            $"Auraly-{receipt.DocumentNumber}",
            renderer.Render(receipt),
            outputDirectory,
            receipt.PaperWidthMillimeters,
            cancellationToken);
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
