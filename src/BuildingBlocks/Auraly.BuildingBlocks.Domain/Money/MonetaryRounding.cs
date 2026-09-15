namespace Auraly.BuildingBlocks.Domain.Money;

public static class MonetaryRounding
{
    public const int LineAmountDecimals = 2;

    public static decimal RoundLineAmount(decimal value) =>
        decimal.Round(value, LineAmountDecimals, MidpointRounding.ToEven);

    public static decimal CeilingLineUnitPrice(decimal value)
    {
        var factor = DecimalPower(LineAmountDecimals);
        return decimal.Ceiling(value * factor) / factor;
    }

    private static decimal DecimalPower(int exponent)
    {
        var result = 1m;
        for (var index = 0; index < exponent; index++) result *= 10m;
        return result;
    }
}
