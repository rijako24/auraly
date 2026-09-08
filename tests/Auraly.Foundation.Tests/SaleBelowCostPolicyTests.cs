using Auraly.Contracts.Sales;

namespace Auraly.Foundation.Tests;

public sealed class SaleBelowCostPolicyTests
{
    [Fact]
    public void Uses_tax_exclusive_net_after_all_discounts()
    {
        Assert.True(SaleBelowCostPolicy.IsBelowCost(2m, 99m, 50m));
        Assert.False(SaleBelowCostPolicy.IsBelowCost(2m, 100m, 50m));
    }
}
