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
            throw new PurchasingValidationException("PresentationName cannot exceed 80 characters.");
        if (line.UnitsPerPresentation <= 0)
            throw new PurchasingValidationException("UnitsPerPresentation must be greater than zero.");

        var presentationQuantity = line.PresentationQuantity;
        if (line.UnitsPerPresentation == 1 &&
            presentation.Equals("Unidad", StringComparison.OrdinalIgnoreCase))
            presentationQuantity = line.Quantity;

        if (presentationQuantity <= 0)
            throw new PurchasingValidationException("PresentationQuantity must be greater than zero.");
        if (presentationQuantity * line.UnitsPerPresentation != line.Quantity)
            throw new PurchasingValidationException(
                "Quantity must equal PresentationQuantity multiplied by UnitsPerPresentation.");

        return line with
        {
            PresentationName = presentation,
            PresentationQuantity = presentationQuantity
        };
    }
}
