using Auraly.Domain.Inventory;

namespace Auraly.Foundation.Tests;

public sealed class InventoryValueAdjustmentTests
{
    [Theory]
    [InlineData(300, 130, 1300)]
    [InlineData(-300, 70, 700)]
    [InlineData(-1000, 0, 0)]
    public void Adjusts_book_value_without_an_acquisition_or_quantity_change(
        decimal adjustment, decimal expectedAverage, decimal expectedValue)
    {
        var result = InventoryValuationCalculator.Calculate(new(10, 100, 10, 1000),
            0, null, InventoryValuationMode.ValueAdjustment, adjustment);

        Assert.Equal(10, result.QuantityAfter);
        Assert.Equal(100, result.AverageUnitCostBefore);
        Assert.Equal(expectedAverage, result.AverageUnitCostAfter);
        Assert.Equal(expectedValue, result.InventoryValueAfter);
        Assert.Equal(adjustment, result.ValueChange);
        Assert.Equal(0, result.RecognizedUnitCost);
    }

    [Fact]
    public void Revalues_the_whole_pool_while_preserving_each_warehouse_quantity()
    {
        var first = InventoryValuationCalculator.Calculate(new(3, 100, 10, 1000),
            0, null, InventoryValuationMode.ValueAdjustment, 300);
        var second = InventoryValuationCalculator.Calculate(new(7, 100, 10, 1000),
            0, null, InventoryValuationMode.ValueAdjustment, 300);

        Assert.Equal(3, first.QuantityAfter);
        Assert.Equal(7, second.QuantityAfter);
        Assert.Equal(130, first.AverageUnitCostAfter);
        Assert.Equal(130, second.AverageUnitCostAfter);
        Assert.Equal(390, first.InventoryValueAfter);
        Assert.Equal(910, second.InventoryValueAfter);
        Assert.Equal(300, first.ValueChange);
    }

    [Fact]
    public void A_subsequent_sale_uses_the_revised_average_without_rewriting_the_prior_sale()
    {
        var priorSale = InventoryValuationCalculator.Calculate(new(12, 100, 12, 1200),
            -2, null, InventoryValuationMode.AverageCost);
        var adjustment = InventoryValuationCalculator.Calculate(new(10, 100, 10, 1000),
            0, null, InventoryValuationMode.ValueAdjustment, 300);
        var nextSale = InventoryValuationCalculator.Calculate(new(10, adjustment.AverageUnitCostAfter,
            10, adjustment.InventoryValueAfter), -2, null, InventoryValuationMode.AverageCost);

        Assert.Equal(100, priorSale.RecognizedUnitCost);
        Assert.Equal(-200, priorSale.ValueChange);
        Assert.Equal(130, nextSale.RecognizedUnitCost);
        Assert.Equal(-260, nextSale.ValueChange);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Does_not_invent_remaining_inventory_for_an_empty_or_negative_pool(decimal quantity)
    {
        Assert.Throws<InvalidOperationException>(() => InventoryValuationCalculator.Calculate(
            new(quantity, 100, quantity, quantity * 100), 0, null,
            InventoryValuationMode.ValueAdjustment, 300));
    }

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public void Requires_a_representable_non_zero_amount(decimal? amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryValuationCalculator.Calculate(
            new(10, 100, 10, 1000), 0, null, InventoryValuationMode.ValueAdjustment, amount));
    }

    [Theory]
    [MemberData(nameof(InvalidPhysicalArguments))]
    public void Cannot_disguise_a_physical_receipt_or_an_acquisition_unit_cost(decimal quantity, decimal? unitCost)
    {
        Assert.Throws<ArgumentException>(() => InventoryValuationCalculator.Calculate(
            new(10, 100, 10, 1000), quantity, unitCost, InventoryValuationMode.ValueAdjustment, 300));
    }

    [Fact]
    public void Cannot_reduce_the_inventory_value_below_zero()
    {
        Assert.Throws<InvalidOperationException>(() => InventoryValuationCalculator.Calculate(
            new(10, 100, 10, 1000), 0, null, InventoryValuationMode.ValueAdjustment, -1000.0001m));
    }

    [Theory]
    [InlineData(InventoryValuationMode.AverageCost)]
    [InlineData(InventoryValuationMode.WeightedAverageReceipt)]
    [InlineData(InventoryValuationMode.SpecifiedCostIssue)]
    public void Existing_physical_modes_cannot_accept_a_value_only_amount(InventoryValuationMode mode)
    {
        Assert.Throws<ArgumentException>(() => InventoryValuationCalculator.Calculate(
            new(10, 100, 10, 1000), 1, 100, mode, 300));
    }

    public static TheoryData<decimal?> InvalidAmounts => new() { null, 0m, 0.00001m };
    public static TheoryData<decimal, decimal?> InvalidPhysicalArguments => new()
    {
        { 1m, null }, { -1m, null }, { 0m, 100m }
    };
}
