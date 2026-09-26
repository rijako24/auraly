using Auraly.Contracts.Purchasing;

namespace Auraly.Application.Purchasing;

internal static class GoodsReceiptLineNormalizer
{
    public static GoodsReceiptLineRequest[] Normalize(IReadOnlyCollection<GoodsReceiptLineRequest> lines) =>
        lines.Select(Normalize).ToArray();

    private static GoodsReceiptLineRequest Normalize(GoodsReceiptLineRequest line)
    {
        var presentation = string.IsNullOrWhiteSpace(line.PresentationName)
            ? "Unidad"
            : line.PresentationName.Trim();
        if (presentation.Length > 80)
            throw new PurchasingValidationException("El nombre de la presentación no puede superar 80 caracteres.");
        if (line.UnitsPerPresentation <= 0)
            throw new PurchasingValidationException("Las unidades por presentación deben ser mayores que cero.");

        var presentationQuantity = line.PresentationQuantity;
        if (line.UnitsPerPresentation == 1 &&
            presentation.Equals("Unidad", StringComparison.OrdinalIgnoreCase))
            presentationQuantity = line.Quantity;

        if (presentationQuantity <= 0)
            throw new PurchasingValidationException("La cantidad de presentaciones debe ser mayor que cero.");
        if (presentationQuantity * line.UnitsPerPresentation != line.Quantity)
            throw new PurchasingValidationException(
                "La cantidad debe coincidir con las presentaciones multiplicadas por sus unidades.");
        if (line.UnitGrossWeightKg is <= 0)
            throw new PurchasingValidationException("El peso bruto unitario debe ser mayor que cero.");

        return line with
        {
            PresentationName = presentation,
            PresentationQuantity = presentationQuantity,
            TotalGrossWeightKg = line.UnitGrossWeightKg is { } unitWeight
                ? decimal.Round(unitWeight * line.Quantity, 6, MidpointRounding.AwayFromZero)
                : line.TotalGrossWeightKg
        };
    }
}
