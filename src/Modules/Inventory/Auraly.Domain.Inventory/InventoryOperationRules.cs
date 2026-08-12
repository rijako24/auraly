namespace Auraly.Domain.Inventory;

public static class InventoryOperationRules
{
    public static decimal CountAdjustment(decimal counted, decimal systemAtBase)
    {
        if (counted < 0) throw new ArgumentOutOfRangeException(nameof(counted));
        return Quantity(counted - systemAtBase);
    }

    public static IReadOnlyList<decimal> AllocateConversionCost(
        decimal inputCost,
        IReadOnlyList<(decimal Quantity, decimal? Weight)> outputs)
    {
        if (inputCost < 0) throw new ArgumentOutOfRangeException(nameof(inputCost));
        if (outputs.Count == 0 || outputs.Any(output => output.Quantity <= 0))
            throw new ArgumentException("Conversion outputs require positive quantities.", nameof(outputs));

        var explicitWeights = outputs.All(output => output.Weight is > 0);
        var denominator = explicitWeights
            ? outputs.Sum(output => output.Weight!.Value)
            : outputs.Sum(output => output.Quantity);
        if (denominator <= 0)
            throw new ArgumentException("Conversion allocation requires a positive denominator.", nameof(outputs));
        if (explicitWeights && decimal.Round(denominator, 6) != 100m)
            throw new ArgumentException("Conversion allocation weights must total 100 percent.", nameof(outputs));

        var result = new decimal[outputs.Count];
        var allocated = 0m;
        for (var index = 0; index < outputs.Count; index++)
        {
            var share = explicitWeights
                ? outputs[index].Weight!.Value / 100m
                : outputs[index].Quantity / denominator;
            result[index] = index == outputs.Count - 1
                ? Money(inputCost - allocated)
                : Money(inputCost * share);
            allocated += result[index];
        }
        return result;
    }

    public static decimal Quantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    public static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
