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
        var quantityAfter = Quantity(quantityBefore + receivedQuantity);
        var receiptValue = Money(receivedQuantity * acquisitionUnitCost);
        var valueAfter = Money(inventoryValueBefore + receiptValue);
        var averageAfter = quantityAfter == 0
            ? 0
            : UnitCost(valueAfter / quantityAfter);
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
