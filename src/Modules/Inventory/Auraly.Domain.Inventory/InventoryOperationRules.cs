namespace Auraly.Domain.Inventory;

public static class InventoryOperationRules
{
    public static ProductConversionEquivalence ValidateConversionEquivalence(
        string conversionType,
        IReadOnlyList<(string Direction, decimal Quantity, decimal Factor)> lines,
        decimal maximumLossPercent)
    {
        if (maximumLossPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumLossPercent));
        if (lines.Count < 2 || lines.Any(line => line.Quantity <= 0 || line.Factor <= 0 || line.Direction is not ("INPUT" or "OUTPUT")))
            throw new ArgumentException("A conversion requires positive input and output quantities and factors.", nameof(lines));

        var inputCount = lines.Count(line => line.Direction == "INPUT");
        var outputCount = lines.Count(line => line.Direction == "OUTPUT");
        if (inputCount == 0 || outputCount == 0 ||
            conversionType == "SPLIT" && inputCount != 1 ||
            conversionType == "MERGE" && outputCount != 1 ||
            conversionType is not ("SPLIT" or "MERGE"))
            throw new ArgumentException("A conversion must be one-to-many or many-to-one.", nameof(lines));

        var equivalents = lines.Select(line => Quantity(line.Quantity * line.Factor)).ToArray();
        var inputEquivalent = Quantity(lines.Select((line, index) => (line, index))
            .Where(item => item.line.Direction == "INPUT")
            .Sum(item => equivalents[item.index]));
        var outputEquivalent = Quantity(lines.Select((line, index) => (line, index))
            .Where(item => item.line.Direction == "OUTPUT")
            .Sum(item => equivalents[item.index]));
        if (inputEquivalent <= 0 || outputEquivalent <= 0)
            throw new ArgumentException("A conversion requires a positive physical input and output.", nameof(lines));
        if (outputEquivalent > inputEquivalent)
            throw new ArgumentException("A conversion cannot produce more equivalent inventory than it consumes.", nameof(lines));

        var lossQuantity = Quantity(inputEquivalent - outputEquivalent);
        var lossPercent = Quantity(lossQuantity / inputEquivalent * 100m);
        if (lossPercent > maximumLossPercent)
            throw new ArgumentException("The conversion loss exceeds the configured family tolerance.", nameof(lines));

        return new(inputEquivalent, outputEquivalent, lossQuantity, lossPercent, equivalents);
    }

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

public sealed record ProductConversionEquivalence(
    decimal InputEquivalent,
    decimal OutputEquivalent,
    decimal LossQuantity,
    decimal LossPercent,
    IReadOnlyList<decimal> EquivalentQuantities);
