using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Auraly.Contracts.Sales;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed class EscPosReceiptRenderer
{
    private static readonly byte[] Initialize = [0x1B, 0x40];
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
    private static readonly byte[] DoubleHeight = [0x1D, 0x21, 0x10];
    private static readonly byte[] NormalSize = [0x1D, 0x21, 0x00];
    private static readonly byte[] Cut = [0x1D, 0x56, 0x41, 0x03];

    public byte[] Render(PosReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var columns = receipt.PaperWidthMillimeters switch
        {
            58 => 32,
            80 => 42,
            _ => throw new ArgumentOutOfRangeException(
                nameof(receipt),
                "Receipt width must be 58 or 80 mm.")
        };
        using var stream = new MemoryStream();
        Write(stream, Initialize);
        Write(stream, AlignCenter);
        Write(stream, DoubleHeight);
        WriteBoldLine(stream, string.IsNullOrWhiteSpace(receipt.CompanyName)
            ? "Auraly"
            : receipt.CompanyName);
        Write(stream, NormalSize);
        var isFiscal = PosSaleDocumentTypes.IsFiscal(receipt.DocumentType);
        WriteBoldLine(stream, PosReceiptPresentation.Title(receipt));
        WriteBoldLine(stream, PosReceiptPresentation.DisplayNumber(receipt));
        WriteLine(stream, receipt.IssuedAt.ToLocalTime()
            .ToString("dd/MM/yyyy, h:mm:ss tt", CultureInfo.GetCultureInfo("es-CO")));
        var scope = Scope(receipt.BusinessName, receipt.WarehouseName);
        if (!string.IsNullOrWhiteSpace(scope))
            WriteWrapped(stream, scope, columns);
        WriteLine(stream, new string('-', columns));
        Write(stream, AlignLeft);
        WriteBoldLine(stream, Pair("Cliente", receipt.CustomerName ?? receipt.CustomerIdentification, columns));
        WriteBoldLine(stream, Pair("Identificacion", receipt.CustomerIdentification, columns));
        WriteLine(stream, new string('-', columns));
        foreach (var line in receipt.Lines)
        {
            WriteBoldWrapped(stream, line.Description, columns);
            WriteLine(
                stream,
                Right(
                    $"{Quantity(line.Quantity)} x {Money(line.UnitPrice)}  {Money(line.Total)}",
                    columns));
            if (line.Discount > 0)
                WriteLine(stream, Right($"Descuento {Money(line.Discount)}", columns));
        }
        WriteLine(stream, new string('-', columns));
        WriteLine(stream, Pair("Subtotal", Money(receipt.UntaxedAmount), columns));
        WriteBoldLine(stream, "Impuestos por tarifa");
        WriteBoldLine(stream, TaxRow("Impuesto", "Base", "Valor", columns));
        foreach (var tax in receipt.Lines
                     .GroupBy(line => new { line.TaxCode, line.TaxRate })
                     .Select(group => new
                     {
                         group.Key.TaxCode,
                         group.Key.TaxRate,
                         Base = group.Sum(line => line.Total - line.Tax),
                         Amount = group.Sum(line => line.Tax)
                     })
                     .OrderBy(value => value.TaxCode, StringComparer.Ordinal)
                     .ThenBy(value => value.TaxRate))
        {
            WriteLine(stream, TaxRow(
                $"{TaxName(tax.TaxCode)} {Rate(tax.TaxRate)}%",
                Money(tax.Base),
                Money(tax.Amount),
                columns));
        }
        WriteLine(stream, Pair("Total impuestos", Money(receipt.TaxAmount), columns));
        if (receipt.WithholdingTotal > 0)
            WriteLine(stream, Pair("Total bruto", Money(receipt.PayableAmount), columns));
        foreach (var withholding in receipt.Withholdings ?? [])
            WriteLine(stream, Pair(
                $"Ret. {withholding.Name}",
                $"-{Money(withholding.Amount)}", columns));
        if (receipt.WithholdingTotal > 0)
            WriteLine(stream, Pair("Total retenciones", $"-{Money(receipt.WithholdingTotal)}", columns));
        WriteBoldLine(stream, Pair(
            "Total",
            Money(receipt.WithholdingTotal > 0 ? receipt.NetPayableAmount : receipt.PayableAmount),
            columns));
        WriteBoldLine(stream, "Medios de pago");
        foreach (var payment in receipt.Payments)
            WriteBoldLine(stream, Pair(PaymentName(payment.MethodCode), Money(payment.Amount), columns));
        WriteLine(stream, new string('-', columns));
        if (isFiscal)
        {
            WriteWrapped(stream, $"CUFE: {receipt.Cufe}", columns);
            Write(stream, AlignCenter);
            WriteQr(stream, receipt.QrPayload!);
            WriteLine(stream, string.Empty);
            WriteLine(stream, "Representacion grafica");
        }
        Write(stream, AlignCenter);
        WriteBoldLine(stream, isFiscal
            ? "Factura emitida por Auraly"
            : "Comprobante emitido por Auraly");
        WriteLine(stream, "www.auralyapp.co");
        WriteLine(stream, string.Empty);
        WriteLine(stream, string.Empty);
        Write(stream, Cut);
        return stream.ToArray();
    }

    private static string Pair(string label, string value, int columns)
    {
        var available = Math.Max(1, columns - value.Length);
        var left = label.Length > available ? label[..available] : label;
        return left.PadRight(available) + value;
    }

    private static string Right(string value, int columns) =>
        value.Length >= columns ? value[..columns] : value.PadLeft(columns);

    private static string TaxRow(string tax, string taxBase, string amount, int columns)
    {
        var taxColumns = columns == 32 ? 8 : 10;
        var valueColumns = (columns - taxColumns) / 2;
        return Fit(tax, taxColumns).PadRight(taxColumns) +
               Fit(taxBase, valueColumns).PadLeft(valueColumns) +
               Fit(amount, columns - taxColumns - valueColumns)
                   .PadLeft(columns - taxColumns - valueColumns);
    }

    private static string Fit(string value, int columns) =>
        value.Length <= columns ? value : value[..columns];

    private static string Money(decimal value) =>
        "$ " + value.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));

    private static string Quantity(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Rate(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string TaxName(string code) => code switch
    {
        "01" => "IVA",
        "02" => "IC",
        "03" => "ICA",
        "04" => "INC",
        _ => code
    };

    private static string PaymentName(string code) => code switch
    {
        "Cash" => "Efectivo",
        "Card" => "Tarjeta",
        "DebitCard" => "Tarjeta debito",
        "CreditCard" => "Tarjeta credito",
        "Transfer" => "Transferencia",
        "Credit" => "Credito / cartera",
        "Voucher" => "Bono / vale",
        "Check" => "Cheque",
        "Withholding" => "Retencion",
        _ => code
    };

    private static string Scope(string? businessName, string? warehouseName)
    {
        var business = string.IsNullOrWhiteSpace(businessName) ? null : $"Sede: {businessName}";
        var warehouse = string.IsNullOrWhiteSpace(warehouseName) ? null : warehouseName;
        return string.Join(" - ", new[] { business, warehouse }.Where(value => value is not null));
    }

    private static void WriteWrapped(Stream stream, string value, int columns)
    {
        var text = Normalize(value);
        for (var offset = 0; offset < text.Length; offset += columns)
            WriteLine(stream, text.Substring(offset, Math.Min(columns, text.Length - offset)));
        if (text.Length == 0) WriteLine(stream, string.Empty);
    }

    private static void WriteBoldWrapped(Stream stream, string value, int columns)
    {
        Write(stream, BoldOn);
        WriteWrapped(stream, value, columns);
        Write(stream, BoldOff);
    }

    private static void WriteBoldLine(Stream stream, string value)
    {
        Write(stream, BoldOn);
        WriteLine(stream, value);
        Write(stream, BoldOff);
    }

    private static void WriteLine(Stream stream, string value)
    {
        Write(stream, Encoding.ASCII.GetBytes(Normalize(value)));
        stream.WriteByte(0x0A);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character <= 0x7F ? character : '?');
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void WriteQr(Stream stream, string payload)
    {
        var data = Encoding.UTF8.GetBytes(payload);
        Write(stream, [0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00]);
        Write(stream, [0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06]);
        Write(stream, [0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31]);
        var length = checked(data.Length + 3);
        Write(stream,
        [
            0x1D, 0x28, 0x6B,
            (byte)(length & 0xFF),
            (byte)((length >> 8) & 0xFF),
            0x31, 0x50, 0x30
        ]);
        Write(stream, data);
        Write(stream, [0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30]);
    }

    private static void Write(Stream stream, ReadOnlySpan<byte> bytes) => stream.Write(bytes);
}

internal static class PosReceiptPresentation
{
    public static string Title(PosReceipt receipt) =>
        PosSaleDocumentTypes.IsFiscal(receipt.DocumentType)
            ? "Factura electronica de venta"
            : "Comprobante de venta";

    public static string DisplayNumber(PosReceipt receipt) =>
        PosSaleDocumentTypes.IsFiscal(receipt.DocumentType) &&
        !string.IsNullOrWhiteSpace(receipt.FiscalNumber)
            ? receipt.FiscalNumber
            : receipt.DocumentNumber;
}

public sealed class FileReceiptPrinter(
    string outputDirectory,
    EscPosReceiptRenderer renderer) : IPosReceiptPrinter
{
    public async Task PrintAsync(
        PosReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("A receipt output directory must be configured.");

        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"{receipt.PrintJobId:N}.escpos");
        var temporary = Path.Combine(directory, $".{receipt.PrintJobId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(
                temporary,
                renderer.Render(receipt),
                cancellationToken);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class WindowsRawReceiptPrinter(
    string printerName,
    EscPosReceiptRenderer renderer,
    IWindowsRawPrintJob? printJob = null) : IPosReceiptPrinter
{
    public Task PrintAsync(PosReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Raw receipt printing requires Windows.");
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("A receipt printer must be configured.");

        var bytes = renderer.Render(receipt);
        (printJob ?? SystemWindowsRawPrintJob.Instance).Print(
            printerName, $"Auraly-{receipt.PrintJobId:N}", bytes);
        return Task.CompletedTask;
    }
}

public interface IWindowsRawPrintJob
{
    void Print(string printerName, string documentName, byte[] bytes);
}

public sealed class SystemWindowsRawPrintJob : IWindowsRawPrintJob
{
    public static SystemWindowsRawPrintJob Instance { get; } = new();

    public void Print(string printerName, string documentName, byte[] bytes) =>
        WindowsRawPrintJob.Print(printerName, documentName, bytes);
}

public static class WindowsRawPrintJob
{
    public static void Print(string name, string documentName, byte[] bytes)
    {
        if (!OpenPrinter(name, out var printer, IntPtr.Zero))
            throw Win32("The configured receipt printer could not be opened.");
        try
        {
            var info = new DocInfo
            {
                DocumentName = documentName,
                DataType = "RAW"
            };
            if (StartDocPrinter(printer, 1, ref info) == 0)
                throw Win32("The receipt print job could not be created.");
            try
            {
                if (!StartPagePrinter(printer))
                    throw Win32("The receipt page could not be started.");
                try
                {
                    if (!WritePrinter(printer, bytes, bytes.Length, out var written) ||
                        written != bytes.Length)
                        throw Win32("The complete receipt could not be sent to the printer.");
                }
                finally
                {
                    EndPagePrinter(printer);
                }
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    private static IOException Win32(string message) =>
        new($"{message} Windows error: {Marshal.GetLastWin32Error()}.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string DocumentName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo info);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrinter(
        IntPtr printer,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] bytes,
        int count,
        out int written);
}
