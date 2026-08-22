using Auraly.Domain.Inventory;

namespace Auraly.Foundation.Tests;

public sealed class InventoryOperationRulesTests
{
    [Fact]
    public void Count_creates_a_difference_against_its_frozen_base()
    {
        Assert.Equal(-2m, InventoryOperationRules.CountAdjustment(18m, 20m));
        Assert.Equal(3.125678m, InventoryOperationRules.CountAdjustment(13.1256784m, 10m));
    }

    [Fact]
    public void Conversion_allocates_the_last_monetary_residue_deterministically()
    {
        var allocated = InventoryOperationRules.AllocateConversionCost(
            10.01m,
            new[] { (1m, (decimal?)33.333333m), (1m, (decimal?)33.333333m), (1m, (decimal?)33.333334m) });

        Assert.Equal(new[] { 3.3367m, 3.3367m, 3.3366m }, allocated);
        Assert.Equal(10.01m, allocated.Sum());
    }

    [Fact]
    public void Conversion_rejects_incomplete_weights()
    {
        Assert.Throws<ArgumentException>(() => InventoryOperationRules.AllocateConversionCost(
            100m,
            new[] { (1m, (decimal?)40m), (1m, (decimal?)40m) }));
    }

    [Fact]
    public void Conversion_uses_linked_product_factors_in_both_directions()
    {
        var split = InventoryOperationRules.ValidateConversionEquivalence(
            "SPLIT", [("INPUT", 10m, 1m), ("OUTPUT", 2m, 2m), ("OUTPUT", 3m, 2m)], 0m);
        var merge = InventoryOperationRules.ValidateConversionEquivalence(
            "MERGE", [("INPUT", 2m, 2m), ("INPUT", 3m, 2m), ("OUTPUT", 10m, 1m)], 0m);

        Assert.Equal(10m, split.InputEquivalent);
        Assert.Equal(10m, split.OutputEquivalent);
        Assert.Equal(0m, split.LossQuantity);
        Assert.Equal(split.InputEquivalent, merge.InputEquivalent);
        Assert.Equal(split.OutputEquivalent, merge.OutputEquivalent);
        Assert.Equal(split.LossQuantity, merge.LossQuantity);
    }

    [Fact]
    public void Conversion_accepts_loss_at_the_configured_boundary()
    {
        var result = InventoryOperationRules.ValidateConversionEquivalence(
            "SPLIT", [("INPUT", 10m, 1m), ("OUTPUT", 9.5m, 1m)], 5m);

        Assert.Equal(0.5m, result.LossQuantity);
        Assert.Equal(5m, result.LossPercent);
    }

    [Theory]
    [InlineData(10, 10.1, 5)]
    [InlineData(10, 9.4, 5)]
    public void Conversion_rejects_overproduction_and_excess_loss(decimal input, decimal output, decimal maximumLoss)
    {
        Assert.Throws<ArgumentException>(() => InventoryOperationRules.ValidateConversionEquivalence(
            "SPLIT", [("INPUT", input, 1m), ("OUTPUT", output, 1m)], maximumLoss));
    }
}
