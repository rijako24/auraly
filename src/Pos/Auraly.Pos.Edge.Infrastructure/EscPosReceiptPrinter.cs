using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed class EscPosReceiptRenderer
{
    private static readonly byte[] Initialize = [0x1B, 0x40];
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
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
        WriteLine(stream, "AURALY");
        WriteLine(stream, "FACTURA ELECTRONICA DE VENTA");
        WriteLine(stream, receipt.FiscalNumber);
        WriteLine(stream, receipt.IssuedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        WriteLine(stream, $"ADQUIRENTE: {receipt.CustomerIdentification}");
        WriteLine(stream, new string('-', columns));
        Write(stream, AlignLeft);
        foreach (var line in receipt.Lines)
        {
            WriteWrapped(stream, $"{line.ProductCode} {line.Description}", columns);
            WriteLine(
                stream,
                Right(
                    $"{Quantity(line.Quantity)} x {Money(line.UnitPrice)}  {Money(line.Total)}",
                    columns));
            if (line.Discount > 0)
                WriteLine(stream, Right($"DESCUENTO {Money(line.Discount)}", columns));
        }
        WriteLine(stream, new string('-', columns));
        WriteLine(stream, Pair("SUBTOTAL", Money(receipt.UntaxedAmount), columns));
        WriteLine(stream, Pair("IMPUESTOS", Money(receipt.TaxAmount), columns));
        WriteLine(stream, Pair("TOTAL", Money(receipt.PayableAmount), columns));
        foreach (var payment in receipt.Payments)
            WriteLine(stream, Pair(payment.MethodCode.ToUpperInvariant(), Money(payment.Amount), columns));
        WriteLine(stream, new string('-', columns));
        WriteWrapped(stream, $"CUFE: {receipt.Cufe}", columns);
        Write(stream, AlignCenter);
        WriteQr(stream, receipt.QrPayload);
        WriteLine(stream, string.Empty);
        WriteLine(stream, "Representacion grafica");
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

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteWrapped(Stream stream, string value, int columns)
    {
        var text = Normalize(value);
        for (var offset = 0; offset < text.Length; offset += columns)
            WriteLine(stream, text.Substring(offset, Math.Min(columns, text.Length - offset)));
        if (text.Length == 0) WriteLine(stream, string.Empty);
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

public sealed class WindowsRawReceiptPrinter(
    string printerName,
    EscPosReceiptRenderer renderer) : IPosReceiptPrinter
{
    public Task PrintAsync(PosReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Raw receipt printing requires Windows.");
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("A receipt printer must be configured.");

        var bytes = renderer.Render(receipt);
        Print(printerName, $"Auraly-{receipt.PrintJobId:N}", bytes);
        return Task.CompletedTask;
    }

    private static void Print(string name, string documentName, byte[] bytes)
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
