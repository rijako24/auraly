using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Domain.Payables;
using Auraly.Domain.Receivables;

namespace Auraly.Foundation.Tests;

public sealed class PortfolioSettlementTests
{
    [Fact]
    public void Shared_settlement_rejects_unknown_nonpositive_and_duplicate_obligations()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => PortfolioSettlement.Create([]));
        Assert.Throws<ArgumentException>(() => PortfolioSettlement.Create([new(Guid.Empty, 1m)]));
        Assert.Throws<ArgumentException>(() => PortfolioSettlement.Create([new(id, 0m)]));
        Assert.Throws<ArgumentException>(() => PortfolioSettlement.Create([new(id, 1m), new(id, 2m)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_wrappers_share_precision_order_and_the_hundred_obligation_boundary(bool payable)
    {
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        if (payable)
        {
            var accepted = PayableSettlement.Create(ids.Take(100).Select(id => new PayableAllocation(id, 1.00005m)));
            Assert.Equal(100.0100m, accepted.TotalAmount);
            Assert.Equal(ids.Take(100), accepted.Allocations.Select(item => item.PayableId));
            Assert.Throws<ArgumentException>(() => PayableSettlement.Create(ids.Select(id => new PayableAllocation(id, 1m))));
        }
        else
        {
            var accepted = ReceivableSettlement.Create(ids.Take(100).Select(id => new ReceivableAllocation(id, 1.00005m)));
            Assert.Equal(100.0100m, accepted.TotalAmount);
            Assert.Equal(ids.Take(100), accepted.Allocations.Select(item => item.ReceivableId));
            Assert.Throws<ArgumentException>(() => ReceivableSettlement.Create(ids.Select(id => new ReceivableAllocation(id, 1m))));
        }
    }

    [Fact]
    public void Wrappers_keep_their_existing_zero_after_rounding_contracts()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => PayableSettlement.Create([new(id, 0.00001m)]));
        Assert.Equal(0m, ReceivableSettlement.Create([new(id, 0.00001m)]).TotalAmount);
    }
}
