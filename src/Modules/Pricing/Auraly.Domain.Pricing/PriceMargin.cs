namespace Auraly.Domain.Pricing;

public static class PriceMargin
{
    public static decimal? CalculateMarginPercent(decimal cost, decimal salePrice)
    {
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
        if (salePrice < 0) throw new ArgumentOutOfRangeException(nameof(salePrice));
        if (salePrice == 0) return null;
        return Percent(100m - (100m * cost / salePrice));
    }

    public static decimal CalculateSalePrice(decimal cost, decimal marginPercent)
    {
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
        if (marginPercent is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(marginPercent));
        return Money(cost / (100m - marginPercent) * 100m);
    }

    public static decimal SuggestedPricePreservingMargin(
        decimal previousCost,
        decimal currentSalePrice,
        decimal newCost)
    {
        var margin = CalculateMarginPercent(previousCost, currentSalePrice);
        return margin is null or < 0
            ? Money(currentSalePrice)
            : CalculateSalePrice(newCost, margin.Value);
    }
    public static decimal RoundPrice(decimal value, decimal increment, string mode)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (increment <= 0) throw new ArgumentOutOfRangeException(nameof(increment));
        var units = value / increment;
        var roundedUnits = mode switch
        {
            "Nearest" => decimal.Round(units, 0, MidpointRounding.AwayFromZero),
            "Up" => decimal.Ceiling(units),
            "Down" => decimal.Floor(units),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return Money(roundedUnits * increment);
    }


    private static decimal Money(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    private static decimal Percent(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
