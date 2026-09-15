using Auraly.BuildingBlocks.Domain.Money;

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
