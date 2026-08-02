using Auraly.Domain.Payables;

namespace Auraly.Foundation.Tests;

public sealed class PayableSettlementTests
{
    [Fact]
    public void Settlement_preserves_distinct_allocations_and_calculates_total()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var settlement = PayableSettlement.Create(
        [
            new PayableAllocation(first, 10_000.12345m),
            new PayableAllocation(second, 5_000m)
        ]);

        Assert.Equal(15_000.1235m, settlement.TotalAmount);
        Assert.Equal(10_000.1235m, settlement.Allocations[0].Amount);
        Assert.Equal(second, settlement.Allocations[1].PayableId);
    }

    [Fact]
    public void Settlement_rejects_empty_duplicate_or_non_positive_allocations()
    {
        var payableId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => PayableSettlement.Create([]));
        Assert.Throws<ArgumentException>(() => PayableSettlement.Create(
        [
            new PayableAllocation(payableId, 1m),
            new PayableAllocation(payableId, 2m)
        ]));
        Assert.Throws<ArgumentException>(() => PayableSettlement.Create(
            [new PayableAllocation(payableId, 0m)]));
        Assert.Throws<ArgumentException>(() => PayableSettlement.Create(
            [new PayableAllocation(Guid.Empty, 1m)]));
    }
}
