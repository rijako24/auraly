using Auraly.Domain.Receivables;

namespace Auraly.Foundation.Tests;

public sealed class ReceivableSettlementTests
{
    [Fact]
    public void Settlement_normalizes_allocations_and_calculates_the_total()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var settlement = ReceivableSettlement.Create([
            new(first, 20_000m),
            new(second, 5_000.12349m)
        ]);

        Assert.Equal(25_000.1235m, settlement.TotalAmount);
        Assert.Equal(20_000m, settlement.Allocations[0].Amount);
        Assert.Equal(5_000.1235m, settlement.Allocations[1].Amount);
    }

    [Fact]
    public void Settlement_rejects_empty_duplicate_or_non_positive_allocations()
    {
        var receivableId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => ReceivableSettlement.Create([]));
        Assert.Throws<ArgumentException>(() => ReceivableSettlement.Create([
            new(receivableId, 1m),
            new(receivableId, 2m)
        ]));
        Assert.Throws<ArgumentException>(() => ReceivableSettlement.Create([
            new(Guid.NewGuid(), 0m)
        ]));
    }
}
