namespace Auraly.Domain.Inventory;

public sealed record InventoryReceiptValuation(
    decimal QuantityAfter,
    decimal AverageUnitCostAfter,
    decimal InventoryValueAfter,
    decimal ReceiptValue);

public static class WeightedAverageCost
{
    public static InventoryReceiptValuation ApplyReceipt(
        decimal quantityBefore,
        decimal inventoryValueBefore,
        decimal receivedQuantity,
        decimal acquisitionUnitCost)
    {
        if (receivedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(receivedQuantity));
        if (acquisitionUnitCost < 0) throw new ArgumentOutOfRangeException(nameof(acquisitionUnitCost));
        var normalizedQuantityBefore = Quantity(quantityBefore);
        var quantityAfter = Quantity(normalizedQuantityBefore + receivedQuantity);
        var receiptValue = Money(receivedQuantity * acquisitionUnitCost);
        decimal averageAfter;
        decimal valueAfter;
        if (normalizedQuantityBefore <= 0)
        {
            // Preserve the real negative balance but treat its weighting quantity
            // as zero. Every receipt therefore establishes a valid current cost,
            // even when the physical balance remains negative after that receipt.
            averageAfter = UnitCost(acquisitionUnitCost);
            valueAfter = Money(quantityAfter * averageAfter);
        }
        else
        {
            var safeValueBefore = Math.Max(0, Money(inventoryValueBefore));
            valueAfter = Money(safeValueBefore + receiptValue);
            averageAfter = UnitCost(valueAfter / quantityAfter);
        }
        return new InventoryReceiptValuation(
            quantityAfter,
            averageAfter,
            valueAfter,
            receiptValue);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    private static decimal UnitCost(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static decimal Quantity(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
