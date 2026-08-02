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
}
