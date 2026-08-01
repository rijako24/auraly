using Auraly.Domain.Returns;

namespace Auraly.Foundation.Tests;

public sealed class SalesReturnAmountCalculatorTests
{
    [Fact]
    public void Final_partial_return_uses_the_exact_original_remainder()
    {
        var result = SalesReturnAmountCalculator.Calculate(
            3m, 1m, 10m, 1.9m, 11.9m,
            2m, .6666m, 6.6666m, 1.2666m, 7.9332m,
            1m);

        Assert.Equal(.3334m, result.DiscountAmount);
        Assert.Equal(3.3334m, result.UntaxedAmount);
        Assert.Equal(.6334m, result.TaxAmount);
        Assert.Equal(3.9668m, result.LineTotal);
    }

    [Fact]
    public void Cumulative_quantity_cannot_exceed_the_original_sale()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            SalesReturnAmountCalculator.Calculate(
                3m, 0m, 30m, 5.7m, 35.7m,
                2m, 0m, 20m, 3.8m, 23.8m,
                2m));

        Assert.Contains("exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
