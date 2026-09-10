namespace Auraly.Domain.Inventory;

public enum InventoryValuationMode
{
    AverageCost,
    WeightedAverageReceipt,
    SpecifiedCostIssue
}

public sealed record InventoryValuationState(
    decimal QuantityOnHand,
    decimal AverageUnitCost,
    decimal PoolQuantityOnHand,
    decimal PoolInventoryValue);

public sealed record InventoryValuationResult(
    decimal QuantityAfter,
    decimal AverageUnitCostBefore,
    decimal AverageUnitCostAfter,
    decimal InventoryValueAfter,
    decimal RecognizedUnitCost,
    decimal ValueChange);

/// <summary>
/// Owns every inventory average-cost calculation. Persistence code supplies a
/// locked state and writes this immutable result; document handlers never
/// calculate balances or average costs themselves.
/// </summary>
public static class InventoryValuationCalculator
{
    public static InventoryValuationResult Calculate(
        InventoryValuationState state,
        decimal quantityChange,
        decimal? specifiedUnitCost,
        InventoryValuationMode mode)
    {
        var quantityBefore = Quantity(state.QuantityOnHand);
        var quantityAfter = Quantity(quantityBefore + quantityChange);
        var poolQuantityBefore = Quantity(state.PoolQuantityOnHand);
        var poolAverageBefore = PoolAverage(
            poolQuantityBefore,
            state.PoolInventoryValue,
            state.AverageUnitCost);
        var recognizedUnitCost = mode == InventoryValuationMode.AverageCost
            ? poolAverageBefore
            : RequiredSpecifiedCost(specifiedUnitCost);
        var valueChange = Money(quantityChange * recognizedUnitCost);

        decimal averageAfter;
        switch (mode)
        {
            case InventoryValuationMode.AverageCost:
                // Issues and uncosted adjustments never erase the last known
                // cost when stock reaches (or crosses) zero.
                averageAfter = poolAverageBefore;
                break;

            case InventoryValuationMode.WeightedAverageReceipt:
                if (quantityChange <= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(quantityChange),
                        "A weighted-average receipt requires a positive quantity.");
                var poolQuantityAfter = Quantity(poolQuantityBefore + quantityChange);
                averageAfter = poolQuantityBefore < 0 && poolQuantityAfter <= 0
                    ? poolAverageBefore
                    : poolQuantityBefore <= 0
                        ? recognizedUnitCost
                        : UnitCost((Math.Max(0m, Money(state.PoolInventoryValue)) + valueChange) /
                            poolQuantityAfter);
                break;

            case InventoryValuationMode.SpecifiedCostIssue:
                if (quantityChange >= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(quantityChange),
                        "A specified-cost issue requires a negative quantity.");
                var specifiedPoolQuantityAfter = Quantity(poolQuantityBefore + quantityChange);
                averageAfter = specifiedPoolQuantityAfter <= 0
                    ? poolAverageBefore
                    : UnitCost((Money(state.PoolInventoryValue) + valueChange) /
                        specifiedPoolQuantityAfter);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (averageAfter < 0)
            throw new InvalidOperationException("The inventory average cost cannot become negative.");

        return new InventoryValuationResult(
            quantityAfter,
            poolAverageBefore,
            averageAfter,
            Money(quantityAfter * averageAfter),
            recognizedUnitCost,
            valueChange);
    }

    private static decimal RequiredSpecifiedCost(decimal? value)
    {
        if (value is null || value < 0)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The valuation mode requires a non-negative unit cost.");
        return UnitCost(value.Value);
    }

    private static decimal PoolAverage(decimal quantity, decimal value, decimal fallback) =>
        quantity != 0m && value / quantity >= 0m
            ? UnitCost(value / quantity)
            : UnitCost(fallback);

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal UnitCost(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static decimal Quantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
