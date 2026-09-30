using Auraly.Domain.Inventory;

namespace Auraly.Foundation.Tests;

public sealed class InventoryDemandResolverTests
{
    [Fact]
    public void Groups_parent_and_linked_presentations_in_the_parent_unit()
    {
        var root = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();

        var demand = Assert.Single(InventoryDemandResolver.Resolve(
        [
            new(parent, parent, root, 1m, 2m, true),
            new(child, child, root, 0.5m, 6m, true)
        ]));

        Assert.Equal(root, demand.InventoryProductId);
        Assert.Equal(5m, demand.RequiredInventoryQuantity);
        Assert.Equal(10m, InventoryDemandResolver.InProductUnits(
            demand.RequiredInventoryQuantity, 0.5m));
    }

    [Fact]
    public void Reserves_only_whole_lines_and_leaves_inventory_for_later_valid_lines()
    {
        var root = Guid.NewGuid();
        var tooLarge = Guid.NewGuid();
        var available = Guid.NewGuid();

        var allocations = InventoryDemandResolver.AllocateWholeLines(
        [
            new(tooLarge, tooLarge, root, 1m, 6m, true),
            new(available, available, root, 0.5m, 4m, true)
        ], new Dictionary<Guid, decimal> { [root] = 5m });

        Assert.False(allocations[0].CanReserve);
        Assert.True(allocations[1].CanReserve);
        Assert.Equal(2m, allocations[1].RequiredInventoryQuantity);
    }

    [Fact]
    public void Reports_every_unavailable_product_in_a_single_validation_batch()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var available = Guid.NewGuid();

        var allocations = InventoryDemandResolver.AllocateWholeLines(
        [
            new(first, first, first, 1m, 2m, true),
            new(second, second, second, 1m, 1m, true),
            new(available, available, available, 1m, 1m, true)
        ], new Dictionary<Guid, decimal>
        {
            [first] = 0m,
            [second] = 0m,
            [available] = 1m
        });

        Assert.Equal([first, second], allocations
            .Where(allocation => !allocation.CanReserve)
            .Select(allocation => allocation.Line.LineId));
    }
}
