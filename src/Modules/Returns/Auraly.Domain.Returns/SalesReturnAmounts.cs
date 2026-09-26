namespace Auraly.Domain.Returns;

public sealed record SalesReturnAmounts(
    decimal DiscountAmount,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal LineTotal);

public static class SalesReturnAmountCalculator
{
    public static decimal AllocatePaymentRounding(
        decimal originalUnrounded,
        decimal originalRounding,
        decimal alreadyReturnedUnrounded,
        decimal alreadyReturnedRounding,
        decimal returnUnrounded)
    {
        if (originalUnrounded <= 0 || returnUnrounded <= 0 ||
            alreadyReturnedUnrounded < 0 ||
            alreadyReturnedUnrounded + returnUnrounded > originalUnrounded)
            throw new ArgumentOutOfRangeException(nameof(returnUnrounded));

        var cumulativeUnrounded = alreadyReturnedUnrounded + returnUnrounded;
        var cumulativeRounding = cumulativeUnrounded == originalUnrounded
            ? originalRounding
            : decimal.Round(originalRounding * cumulativeUnrounded / originalUnrounded,
                4, MidpointRounding.AwayFromZero);
        return cumulativeRounding - alreadyReturnedRounding;
    }

    public static SalesReturnAmounts Calculate(
        decimal originalQuantity,
        decimal originalDiscount,
        decimal originalUntaxed,
        decimal originalTax,
        decimal originalTotal,
        decimal alreadyReturnedQuantity,
        decimal alreadyReturnedDiscount,
        decimal alreadyReturnedUntaxed,
        decimal alreadyReturnedTax,
        decimal alreadyReturnedTotal,
        decimal requestedQuantity)
    {
        if (originalQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(originalQuantity));
        if (requestedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        if (alreadyReturnedQuantity < 0 || alreadyReturnedQuantity + requestedQuantity > originalQuantity)
            throw new ArgumentException("The cumulative return quantity exceeds the original sale line.");
        if (new[] { originalDiscount, originalUntaxed, originalTax, originalTotal,
                    alreadyReturnedDiscount, alreadyReturnedUntaxed,
                    alreadyReturnedTax, alreadyReturnedTotal }.Any(value => value < 0))
            throw new ArgumentException("Return amounts cannot be negative.");

        var completesLine = alreadyReturnedQuantity + requestedQuantity == originalQuantity;
        return new SalesReturnAmounts(
            Amount(originalDiscount, alreadyReturnedDiscount, requestedQuantity,
                originalQuantity, completesLine),
            Amount(originalUntaxed, alreadyReturnedUntaxed, requestedQuantity,
                originalQuantity, completesLine),
            Amount(originalTax, alreadyReturnedTax, requestedQuantity,
                originalQuantity, completesLine),
            Amount(originalTotal, alreadyReturnedTotal, requestedQuantity,
                originalQuantity, completesLine));
    }

    private static decimal Amount(
        decimal original,
        decimal alreadyReturned,
        decimal requestedQuantity,
        decimal originalQuantity,
        bool completesLine)
    {
        if (completesLine) return original - alreadyReturned;
        var amount = decimal.Round(
            original * requestedQuantity / originalQuantity,
            4,
            MidpointRounding.AwayFromZero);
        if (alreadyReturned + amount > original) return original - alreadyReturned;
        return amount;
    }
}
