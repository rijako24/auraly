namespace Auraly.Domain.Inventory;

public enum InventoryValuationMode
{
    AverageCost,
    WeightedAverageReceipt,
    SpecifiedCostIssue,
    ValueAdjustment
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
        InventoryValuationMode mode) =>
        Calculate(state, quantityChange, specifiedUnitCost, mode, null);

    public static InventoryValuationResult Calculate(
        InventoryValuationState state,
        decimal quantityChange,
        decimal? specifiedUnitCost,
        InventoryValuationMode mode,
        decimal? valueAdjustmentAmount)
    {
        if (mode == InventoryValuationMode.ValueAdjustment)
            return AdjustValue(state, quantityChange, specifiedUnitCost, valueAdjustmentAmount);
        if (valueAdjustmentAmount is not null)
            throw new ArgumentException("Only a value adjustment can specify an inventory value change.", nameof(valueAdjustmentAmount));
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
        var acquisitionValue = Money(quantityChange * recognizedUnitCost);

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
                        : UnitCost((Math.Max(0m, Money(state.PoolInventoryValue)) + acquisitionValue) /
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
                    : UnitCost((Money(state.PoolInventoryValue) + acquisitionValue) /
                        specifiedPoolQuantityAfter);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (averageAfter < 0)
            throw new InvalidOperationException("The inventory average cost cannot become negative.");

        // A movement changes the book value of the cost pool. A receipt that
        // covers negative stock can differ from its acquisition amount; the
        // accounting entry must recognize that difference in cost of sales.
        var poolQuantityAfterValuation = Quantity(poolQuantityBefore + quantityChange);
        var valueChange = Money(poolQuantityAfterValuation * averageAfter) -
            Money(state.PoolInventoryValue);
        return new InventoryValuationResult(
            quantityAfter,
            poolAverageBefore,
            averageAfter,
            Money(quantityAfter * averageAfter),
            recognizedUnitCost,
            valueChange);
    }

    private static InventoryValuationResult AdjustValue(
        InventoryValuationState state, decimal quantityChange, decimal? specifiedUnitCost, decimal? amount)
    {
        if (quantityChange != 0m || specifiedUnitCost is not null)
            throw new ArgumentException("A value adjustment cannot change quantity or specify an acquisition unit cost.");
        if (amount is null || Money(amount.Value) == 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "A value adjustment requires a non-zero monetary amount.");
        var poolQuantity = Quantity(state.PoolQuantityOnHand);
        if (poolQuantity <= 0m)
            throw new InvalidOperationException("A value adjustment requires positive remaining inventory in its cost pool.");
        var poolValue = Money(state.PoolInventoryValue);
        var adjustedValue = poolValue + Money(amount.Value);
        if (poolValue < 0m || adjustedValue < 0m)
            throw new InvalidOperationException("A value adjustment cannot produce a negative inventory value.");
        var averageBefore = PoolAverage(poolQuantity, poolValue, state.AverageUnitCost);
        var averageAfter = UnitCost(adjustedValue / poolQuantity);
        var quantity = Quantity(state.QuantityOnHand);
        // The same quantity/unit-cost scales as physical movements apply. The
        // writer returns the exact aggregate delta of the rounded stored values;
        // callers must use that book delta for the accounting counterpart.
        return new(quantity, averageBefore, averageAfter, Money(quantity * averageAfter),
            0m, Money(poolQuantity * averageAfter) - poolValue);
    }

    private static decimal RequiredSpecifiedCost(decimal? value)
    {
        if (value is null || value < 0)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The valuation mode requires a non-negative unit cost.");
        return UnitCost(value.Value);
    }

    private static decimal PoolAverage(decimal quantity, decimal value, decimal fallback)
    {
        if (quantity > 0m)
            return value > 0m ? UnitCost(value / quantity) : 0m;
        return quantity < 0m && value / quantity > 0m
            ? UnitCost(value / quantity)
            : UnitCost(fallback);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal UnitCost(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static decimal Quantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
