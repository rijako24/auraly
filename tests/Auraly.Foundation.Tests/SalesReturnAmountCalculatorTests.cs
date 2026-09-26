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

    [Theory]
    [InlineData(30, "13.5", "16.5")]
    [InlineData(-40, "-18", "-22")]
    public void Partial_returns_allocate_the_original_payment_rounding_without_residual(
        int originalRounding, string firstExpected, string secondExpected)
    {
        const decimal original = 2770m;
        var first = SalesReturnAmountCalculator.AllocatePaymentRounding(
            original, originalRounding, 0m, 0m, 1246.5m);
        var second = SalesReturnAmountCalculator.AllocatePaymentRounding(
            original, originalRounding, 1246.5m, first, 1523.5m);

        Assert.Equal(decimal.Parse(firstExpected, System.Globalization.CultureInfo.InvariantCulture), first);
        Assert.Equal(decimal.Parse(secondExpected, System.Globalization.CultureInfo.InvariantCulture), second);
        Assert.Equal(original + originalRounding,
            1246.5m + first + 1523.5m + second);
    }

    [Fact]
    public void A_small_final_return_never_receives_the_entire_negative_rounding()
    {
        var first = SalesReturnAmountCalculator.AllocatePaymentRounding(
            11940m, -40m, 0m, 0m, 11930m);
        var last = SalesReturnAmountCalculator.AllocatePaymentRounding(
            11940m, -40m, 11930m, first, 10m);

        Assert.True(10m + last > 0m);
        Assert.Equal(11900m, 11930m + first + 10m + last);
    }
}
