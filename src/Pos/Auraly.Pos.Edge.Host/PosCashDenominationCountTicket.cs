using System.Globalization;
using System.Net;
using System.Text;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PosCashDenominationCountLine(
    string Label,
    decimal Value,
    int Quantity,
    decimal Subtotal);

public sealed record PosCashDenominationCountTicket(
    string BusinessName,
    string UserName,
    DateTimeOffset CountedAt,
    IReadOnlyList<PosCashDenominationCountLine> Lines,
    decimal Total);

public sealed class PosCashDenominationCountTicketPrinter(
    PosPrinterConfigurationStore configuration,
    IWindowsRawPrintJob rawPrintJob,
    IWindowsRenderedPrintJob renderedPrintJob,
    PosWorkstationIdentity? workstation = null)
{
    public Task PrintAsync(
        PosCashDenominationCountTicket ticket,
        CancellationToken cancellationToken)
    {
        Validate(ticket);
        var settings = configuration.Load();
        var printerName = settings.PosPrinterName;
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException(
                "Configura la impresora de facturación en formato tirilla para imprimir el conteo.");

        var name = $"Conteo-efectivo-{ticket.CountedAt:yyyyMMdd-HHmmss}";
        if (WindowsPrinterOutput.RequiresRenderedDocument(printerName) ||
            !string.IsNullOrWhiteSpace(workstation?.CompanyLogoSource))
            return renderedPrintJob.PrintAsync(
                printerName, name,
                RenderHtml(ticket, workstation, settings.ReceiptPaperWidthMillimeters),
                configuration.ReceiptOutputDirectory,
                settings.ReceiptPaperWidthMillimeters,
                cancellationToken);
        rawPrintJob.Print(
            printerName, name,
            RenderRaw(ticket, workstation, settings.ReceiptPaperWidthMillimeters));
        return Task.CompletedTask;
    }

    internal static void Validate(PosCashDenominationCountTicket ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket.BusinessName) ||
            string.IsNullOrWhiteSpace(ticket.UserName) || ticket.CountedAt == default ||
            ticket.Lines.Count == 0 || ticket.Lines.Any(line =>
                string.IsNullOrWhiteSpace(line.Label) || line.Value <= 0 ||
                line.Quantity <= 0 || line.Subtotal != line.Value * line.Quantity) ||
            ticket.Total != ticket.Lines.Sum(line => line.Subtotal))
            throw new ArgumentException("El conteo de denominaciones no es válido.");
    }

    internal static byte[] RenderRaw(
        PosCashDenominationCountTicket ticket,
        PosWorkstationIdentity? workstation,
        int paperWidthMillimeters)
    {
        var columns = paperWidthMillimeters == 58 ? 32 : paperWidthMillimeters == 80 ? 42
            : throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters));
        using var stream = new MemoryStream();
        Write(stream, [0x1B, 0x40, 0x1B, 0x61, 0x00]);
        Bold(stream, workstation?.CompanyName ?? ticket.BusinessName);
        Bold(stream, "Conteo de efectivo");
        Line(stream, $"Sede: {ticket.BusinessName}");
        Line(stream, $"Responsable: {ticket.UserName}");
        Line(stream, $"Fecha: {ticket.CountedAt.ToLocalTime():dd/MM/yyyy HH:mm}");
        Line(stream, new string('-', columns));
        foreach (var item in ticket.Lines)
        {
            Wrapped(stream, item.Label, columns);
            Line(stream, Pair(
                $"{item.Quantity} x {Money(item.Value)}",
                Money(item.Subtotal), columns));
        }
        Line(stream, new string('-', columns));
        Bold(stream, Pair("Total", Money(ticket.Total), columns));
        Line(stream, string.Empty);
        Line(stream, string.Empty);
        Write(stream, [0x1D, 0x56, 0x41, 0x03]);
        return stream.ToArray();
    }

    internal static string RenderHtml(
        PosCashDenominationCountTicket ticket,
        PosWorkstationIdentity? workstation,
        int paperWidthMillimeters)
    {
        if (paperWidthMillimeters is not (58 or 80))
            throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters));
        var rows = string.Join(string.Empty, ticket.Lines.Select(line =>
            $"<tr><td>{Encode(line.Label)}</td><td>{line.Quantity}</td><td>{Money(line.Value)}</td><th>{Money(line.Subtotal)}</th></tr>"));
        var logo = string.IsNullOrWhiteSpace(workstation?.CompanyLogoSource)
            ? string.Empty
            : $"<img src=\"{Encode(workstation.CompanyLogoSource)}\" alt=\"Logo\">";
        return $$"""
<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Conteo de efectivo</title><style>@page{size:{{paperWidthMillimeters}}mm auto;margin:4mm}*{box-sizing:border-box}body{width:{{paperWidthMillimeters - 8}}mm;margin:auto;font:10px/1.4 Arial,sans-serif;color:#111}img{display:block;max-width:48mm;max-height:18mm;object-fit:contain;margin:0 auto 3mm}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:7px}h1{font-size:15px;margin:3px 0;font-weight:800}p{margin:3px 0}table{width:100%;border-collapse:collapse;margin-top:8px}th,td{padding:4px 1px;border-bottom:1px dashed #999;text-align:left}td:nth-child(n+2),th{text-align:right}.total{display:flex;justify-content:space-between;border-block:2px solid #111;margin-top:10px;padding:8px 0;font-size:14px;font-weight:800}</style></head><body><header>{{logo}}<h1>{{Encode(workstation?.CompanyName ?? ticket.BusinessName)}}</h1><p><strong>Conteo de efectivo</strong></p><p>Sede: <strong>{{Encode(ticket.BusinessName)}}</strong></p><p>Responsable: <strong>{{Encode(ticket.UserName)}}</strong></p><p>Fecha: <strong>{{ticket.CountedAt.ToLocalTime():dd/MM/yyyy HH:mm}}</strong></p></header><table><thead><tr><th>Denom.</th><th>Cant.</th><th>Valor</th><th>Subtotal</th></tr></thead><tbody>{{rows}}</tbody></table><div class="total"><span>Total contado</span><strong>{{Money(ticket.Total)}}</strong></div></body></html>
""";
    }

    private static string Money(decimal value) =>
        value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string Pair(string left, string right, int width) =>
        Normalize(left)[..Math.Min(Normalize(left).Length, Math.Max(1, width - right.Length))]
            .PadRight(Math.Max(1, width - right.Length)) + right;
    private static void Wrapped(Stream stream, string value, int width)
    {
        var normalized = Normalize(value);
        for (var offset = 0; offset < normalized.Length; offset += width)
            Line(stream, normalized.Substring(offset, Math.Min(width, normalized.Length - offset)));
    }
    private static void Line(Stream stream, string value)
    {
        Write(stream, Encoding.ASCII.GetBytes(Normalize(value)));
        stream.WriteByte(0x0A);
    }
    private static void Bold(Stream stream, string value)
    {
        Write(stream, [0x1B, 0x45, 0x01]);
        Line(stream, value);
        Write(stream, [0x1B, 0x45, 0x00]);
    }
    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(character =>
                CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => character <= 0x7F ? character : '?').ToArray())
            .Normalize(NormalizationForm.FormC);
    }
    private static void Write(Stream stream, ReadOnlySpan<byte> bytes) => stream.Write(bytes);
}
