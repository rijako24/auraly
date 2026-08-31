using System.Globalization;
using System.Net;
using System.Text;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PosCashMovementTicket(
    Guid DocumentId,
    string Direction,
    string ReasonName,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Reference,
    string? Notes);

public sealed class PosCashMovementTicketPrinter(
    PosPrinterConfigurationStore configuration,
    IWindowsRawPrintJob rawPrintJob,
    IWindowsRenderedPrintJob renderedPrintJob,
    PosWorkstationIdentity workstation)
{
    public Task PrintAsync(
        PosCashMovementTicket ticket,
        PosLocalUserSession session,
        CancellationToken cancellationToken)
    {
        if (ticket.DocumentId == Guid.Empty || ticket.Amount <= 0 ||
            ticket.Direction is not ("In" or "Out") ||
            string.IsNullOrWhiteSpace(ticket.ReasonName))
            throw new ArgumentException("El movimiento para imprimir no es válido.");

        var settings = configuration.Load();
        if (settings.PosOutputFormat != PrintTemplateFormats.Receipt)
            throw new InvalidOperationException(
                "El ticket de entrada o salida solo está soportado cuando facturación usa formato tirilla.");
        var printerName = settings.PosPrinterName ?? settings.ReceiptPrinterName;
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException(
                "Configura la impresora de facturación en formato tirilla para imprimir este ticket.");

        var documentName = $"Movimiento-{ticket.DocumentId:N}";
        if (WindowsPrinterOutput.RequiresRenderedDocument(printerName) ||
            !string.IsNullOrWhiteSpace(workstation.CompanyLogoSource))
            return renderedPrintJob.PrintAsync(
                printerName,
                documentName,
                RenderHtml(ticket, session, workstation),
                configuration.ReceiptOutputDirectory,
                cancellationToken);

        rawPrintJob.Print(
            printerName,
            documentName,
            RenderRaw(ticket, session, workstation, settings.ReceiptPaperWidthMillimeters));
        return Task.CompletedTask;
    }

    internal static byte[] RenderRaw(
        PosCashMovementTicket ticket,
        PosLocalUserSession session,
        PosWorkstationIdentity workstation,
        int width)
    {
        var columns = width == 58 ? 32 : width == 80 ? 42
            : throw new ArgumentOutOfRangeException(nameof(width));
        using var stream = new MemoryStream();
        Write(stream, [0x1B, 0x40, 0x1B, 0x61, 0x01]);
        Line(stream, workstation.CompanyName);
        Line(stream, ticket.Direction == "In" ? "ENTRADA DE DINERO" : "SALIDA DE DINERO");
        Line(stream, workstation.BusinessName);
        Line(stream, workstation.WarehouseName);
        Line(stream, new string('-', columns));
        Write(stream, [0x1B, 0x61, 0x00]);
        Wrapped(stream, $"MOTIVO: {ticket.ReasonName}", columns);
        Line(stream, Pair("VALOR", Money(ticket.Amount), columns));
        Wrapped(stream, $"REFERENCIA: {ticket.Reference ?? "-"}", columns);
        Wrapped(stream, $"OBSERVACION: {ticket.Notes ?? "-"}", columns);
        Wrapped(stream, $"RESPONSABLE: {session.DisplayName}", columns);
        Line(stream, $"FECHA: {ticket.OccurredAt:yyyy-MM-dd HH:mm:ss}");
        Line(stream, new string('-', columns));
        Line(stream, string.Empty);
        Line(stream, "FIRMA: ______________________");
        Line(stream, string.Empty);
        Wrapped(stream, $"MOVIMIENTO: {ticket.DocumentId:D}", columns);
        Line(stream, string.Empty);
        Write(stream, [0x1D, 0x56, 0x41, 0x03]);
        return stream.ToArray();
    }

    internal static string RenderHtml(
        PosCashMovementTicket ticket,
        PosLocalUserSession session,
        PosWorkstationIdentity workstation)
    {
        var logo = string.IsNullOrWhiteSpace(workstation.CompanyLogoSource)
            ? string.Empty
            : $"<img src=\"{Encode(workstation.CompanyLogoSource)}\" alt=\"Logo\">";
        return $$"""
<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Movimiento de caja</title><style>@page{size:80mm auto;margin:4mm}body{width:72mm;margin:auto;font:10px Arial,sans-serif;color:#111}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:8px}img{display:block;max-width:48mm;max-height:18mm;object-fit:contain;margin:0 auto 3mm}h1{font-size:15px;margin:3px}h2{font-size:12px;margin:5px}.amount{display:flex;justify-content:space-between;border-block:2px solid #111;padding:8px 0;margin:10px 0;font-size:14px;font-weight:800}.detail{line-height:1.5;overflow-wrap:anywhere}.signature{margin-top:18mm;border-top:1px solid #111;text-align:center;padding-top:3px}.id{margin-top:10px;font-size:8px;overflow-wrap:anywhere}</style></head><body><header>{{logo}}<h1>{{Encode(workstation.CompanyName)}}</h1><h2>{{(ticket.Direction == "In" ? "ENTRADA DE DINERO" : "SALIDA DE DINERO")}}</h2><p>{{Encode(workstation.BusinessName)}} · {{Encode(workstation.WarehouseName)}}</p></header><div class="amount"><span>VALOR</span><span>{{Money(ticket.Amount)}}</span></div><div class="detail"><p><strong>Motivo:</strong> {{Encode(ticket.ReasonName)}}</p><p><strong>Referencia:</strong> {{Encode(ticket.Reference ?? "-")}}</p><p><strong>Observación:</strong> {{Encode(ticket.Notes ?? "-")}}</p><p><strong>Responsable:</strong> {{Encode(session.DisplayName)}}</p><p><strong>Fecha:</strong> {{ticket.OccurredAt:yyyy-MM-dd HH:mm:ss zzz}}</p></div><div class="signature">Firma</div><p class="id">Movimiento: {{ticket.DocumentId:D}}</p></body></html>
""";
    }

    private static string Money(decimal value) =>
        value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string Pair(string label, string value, int columns)
    {
        var available = Math.Max(1, columns - value.Length);
        return label[..Math.Min(label.Length, available)].PadRight(available) + value;
    }
    private static void Wrapped(Stream stream, string value, int columns)
    {
        var text = Normalize(value);
        for (var offset = 0; offset < text.Length; offset += columns)
            Line(stream, text.Substring(offset, Math.Min(columns, text.Length - offset)));
        if (text.Length == 0) Line(stream, string.Empty);
    }
    private static void Line(Stream stream, string value)
    {
        Write(stream, Encoding.ASCII.GetBytes(Normalize(value)));
        stream.WriteByte(0x0A);
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
