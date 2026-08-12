using Auraly.Domain.Purchasing;

namespace Auraly.Foundation.Tests;

public sealed class PurchaseReturnCalculatorTests
{
    [Fact]
    public void Partial_and_final_allocations_preserve_original_totals()
    {
        var first=PurchaseReturnCalculator.Allocate(
            3m,0m,1m,300m,0m,3_000m,0m,570m,0m,3_570m,0m);
        var final=PurchaseReturnCalculator.Allocate(
            3m,1m,2m,300m,first.DiscountAmount,3_000m,first.NetAmount,
            570m,first.TaxAmount,3_570m,first.LineTotal);
        Assert.Equal(3_000m,first.NetAmount+final.NetAmount);
        Assert.Equal(570m,first.TaxAmount+final.TaxAmount);
        Assert.Equal(3_570m,first.LineTotal+final.LineTotal);
    }

    [Fact]
    public void Quantity_above_remaining_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            PurchaseReturnCalculator.Allocate(
                5m,4m,2m,0m,0m,5_000m,4_000m,0m,0m,5_000m,4_000m));
    }
}