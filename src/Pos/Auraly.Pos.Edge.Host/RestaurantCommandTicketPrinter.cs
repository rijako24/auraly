using System.Globalization;
using System.Text;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PrintRestaurantCommandRequest(
    string ProfileKey, RestaurantCommandCardView Card, bool IsCopy = false,
    string PrintKind = "Original", decimal? RemovedQuantity = null,
    string? RemovedProductName = null);

public sealed class RestaurantCommandTicketPrinter(
    PosPrinterConfigurationStore configuration,
    IWindowsRawPrintJob printJob)
{
    public Task PrintAsync(PrintRestaurantCommandRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Las comandas físicas requieren un receptor Windows.");
        if (request.Card.CardId == Guid.Empty ||
            request.PrintKind is not ("Original" or "Correction") ||
            (request.PrintKind == "Correction" &&
             (request.RemovedQuantity is null or <= 0 ||
              string.IsNullOrWhiteSpace(request.RemovedProductName))) ||
            request.Card.Lines.Count == 0 ||
            request.Card.Lines.Count > 100 ||
            request.Card.Lines.Any(line => line.Quantity <= 0 ||
                string.IsNullOrWhiteSpace(line.ProductName)))
            throw new ArgumentException("La comanda no tiene líneas válidas.");
        var profile = configuration.CommandProfiles().FirstOrDefault(item =>
            string.Equals(item.Key, request.ProfileKey,
                StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException(
                "El perfil de impresora de esta comanda no está configurado.");
        var bytes = RestaurantCommandTicketRenderer.Render(request.Card,
            profile.PaperWidthMillimeters, request.IsCopy, profile.Cut,
            request.PrintKind, request.RemovedQuantity,
            request.RemovedProductName);
        for (var copy = 0; copy < profile.Copies; copy++)
        {
            ct.ThrowIfCancellationRequested();
            printJob.Print(profile.PrinterName,
                $"Auraly-Comanda-{request.Card.CardId:N}-{copy + 1}", bytes);
        }
        return Task.CompletedTask;
    }
}

public static class RestaurantCommandTicketRenderer
{
    public static byte[] Render(RestaurantCommandCardView card,
        int paperWidthMillimeters, bool isCopy = false, bool cut = true,
        string printKind = "Original", decimal? removedQuantity = null,
        string? removedProductName = null)
    {
        var columns = paperWidthMillimeters switch
        {
            58 => 32,
            80 => 42,
            _ => throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters))
        };
        using var output = new MemoryStream();
        output.Write([0x1B, 0x40, 0x1B, 0x61, 0x01, 0x1B, 0x45, 0x01]);
        var heading = printKind == "Correction"
            ? card.State == "Cancelled" ? "COMANDA ANULADA" :
                "COMANDA ACTUALIZADA" : "COMANDA";
        Line(output, isCopy ? $"{heading} - COPIA" : heading);
        Line(output, card.StationName);
        output.Write([0x1B, 0x45, 0x00, 0x1B, 0x61, 0x00]);
        Line(output, $"Mesa {card.TableCode}  {card.ReceivedAt.ToLocalTime():dd/MM HH:mm}");
        Line(output, $"Ronda {card.CardId.ToString("N")[..8].ToUpperInvariant()}");
        Line(output, new string('-', columns));
        if (printKind == "Correction" && removedQuantity is > 0 &&
            !string.IsNullOrWhiteSpace(removedProductName))
        {
            output.Write([0x1B, 0x45, 0x01]);
            Wrap(output, $"RETIRAR {removedQuantity.Value.ToString("0.###", CultureInfo.InvariantCulture)} x {removedProductName}", columns);
            output.Write([0x1B, 0x45, 0x00]);
            Line(output, new string('-', columns));
        }
        if (card.State == "Cancelled")
            Line(output, "Sin productos activos en esta comanda");
        else foreach (var item in card.Lines)
        {
            output.Write([0x1B, 0x45, 0x01]);
            Wrap(output, $"{item.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} x {item.ProductName}", columns);
            output.Write([0x1B, 0x45, 0x00]);
            if (!string.IsNullOrWhiteSpace(item.PreparationNote))
                Wrap(output, $"  * {item.PreparationNote}", columns);
            Wrap(output, $"  {item.AddedByName}", columns);
        }
        Line(output, new string('-', columns));
        Line(output, "Auraly - Estacion de preparacion");
        if (cut) output.Write(EscPosThermalCommands.MinimumFeedAndPartialCut);
        else output.Write([0x1B, 0x64, 0x03]);
        return output.ToArray();
    }

    private static void Wrap(Stream output, string value, int columns)
    {
        var normalized = Normalize(value);
        for (var offset = 0; offset < normalized.Length; offset += columns)
            Line(output, normalized.Substring(offset,
                Math.Min(columns, normalized.Length - offset)));
    }

    private static void Line(Stream output, string value)
    {
        output.Write(Encoding.ASCII.GetBytes(Normalize(value)));
        output.WriteByte(0x0A);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) ==
                UnicodeCategory.NonSpacingMark) continue;
            output.Append(character is >= ' ' and <= '~' ? character : '?');
        }
        return output.ToString();
    }
}
