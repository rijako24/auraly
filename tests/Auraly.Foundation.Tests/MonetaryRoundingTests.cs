using Auraly.BuildingBlocks.Domain.Money;
using Auraly.Contracts.Sales;

namespace Auraly.Foundation.Tests;

public sealed class MonetaryRoundingTests
{
    [Theory]
    [InlineData("3999.9901", "4000.00")]
    [InlineData("2101.0000", "2101.00")]
    [InlineData("0.001", "0.01")]
    public void Public_unit_price_is_always_rounded_up(string raw, string expected)
    {
        Assert.Equal(
            decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            MonetaryRounding.CeilingLineUnitPrice(
                decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)));
    }
}

public sealed class PosPaymentRoundingPolicyTests
{
    [Theory]
    [InlineData("10450.45", "49.55")]
    [InlineData("10749", "-49")]
    [InlineData("10750", "50")]
    [InlineData("10800", "0")]
    public void Invoice_rounds_to_the_nearest_hundred(
        string total,
        string expectedAdjustment)
    {
        var original = decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture);
        var adjustment = PosPaymentRoundingPolicy.Adjustment(original);

        Assert.Equal(
            decimal.Parse(expectedAdjustment, System.Globalization.CultureInfo.InvariantCulture),
            adjustment);
        Assert.Equal(
            decimal.Round(original / 100m, 0, MidpointRounding.AwayFromZero) * 100m,
            original + adjustment);
    }

    [Fact]
    public void Collection_adjustment_is_kept_separate_from_the_applied_payment()
    {
        const decimal invoiceTotal = 10_450.45m;
        var adjustment = PosPaymentRoundingPolicy.Adjustment(invoiceTotal);
        var payment = new PosSalePaymentContract(
            1, "Cash", invoiceTotal, null,
            TenderedAmount: 20_000m, RoundingAdjustment: adjustment);

        Assert.True(PosPaymentRoundingPolicy.IsValid(invoiceTotal, [payment]));
        Assert.Equal(10_500m, payment.CollectedAmount);
        Assert.Equal(invoiceTotal, payment.Amount);
    }
}
