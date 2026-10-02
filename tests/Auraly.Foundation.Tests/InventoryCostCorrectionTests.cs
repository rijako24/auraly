using Auraly.Domain.Inventory;

namespace Auraly.Foundation.Tests;

public sealed class InventoryCostCorrectionTests
{
    [Fact]
    public void Restores_positive_cost_without_moving_stock_and_allows_the_next_sale()
    {
        var corrected = InventoryValuationCalculator.Calculate(
            new InventoryValuationState(20.5m, 0m, 28.5m, 0m),
            0m, 2560.016m, InventoryValuationMode.CostCorrection);

        Assert.Equal(20.5m, corrected.QuantityAfter);
        Assert.Equal(2560.016m, corrected.AverageUnitCostAfter);
        Assert.Equal(72960.4560m, corrected.ValueChange);

        var sale = InventoryValuationCalculator.Calculate(
            new InventoryValuationState(20.5m, corrected.AverageUnitCostAfter,
                28.5m, 72960.4560m),
            -0.5m, null, InventoryValuationMode.AverageCost);
        Assert.Equal(2560.016m, sale.RecognizedUnitCost);
        Assert.Equal(20m, sale.QuantityAfter);
    }

    [Fact]
    public void Damaged_stock_revaluation_to_zero_has_only_a_book_value_effect()
    {
        var corrected = InventoryValuationCalculator.Calculate(
            new InventoryValuationState(5.5m, 1000m, 5.5m, 5500m),
            0m, 0m, InventoryValuationMode.CostCorrection);

        Assert.Equal(5.5m, corrected.QuantityAfter);
        Assert.Equal(0m, corrected.AverageUnitCostAfter);
        Assert.Equal(-5500m, corrected.ValueChange);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryValuationCalculator.Calculate(
                new InventoryValuationState(5.5m, 1000m, 5.5m, 5500m),
                -1m, 0m, InventoryValuationMode.CostCorrection));
    }
}
