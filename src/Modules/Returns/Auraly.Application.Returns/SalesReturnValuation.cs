using Auraly.Domain.Returns;

namespace Auraly.Application.Returns;

// Public valuation boundary for delivery capture. Returns keeps ownership of
// its monetary rules; consumers receive values, never internal domain models.
public static class SalesReturnValuation
{
    public static decimal LineTotal(
        decimal originalQuantity, decimal originalDiscount, decimal originalUntaxed,
        decimal originalTax, decimal originalTotal, decimal returnedQuantity,
        decimal returnedDiscount, decimal returnedUntaxed, decimal returnedTax,
        decimal returnedTotal, decimal requestedQuantity) =>
        SalesReturnAmountCalculator.Calculate(
            originalQuantity, originalDiscount, originalUntaxed, originalTax,
            originalTotal, returnedQuantity, returnedDiscount, returnedUntaxed,
            returnedTax, returnedTotal, requestedQuantity).LineTotal;

    public static decimal PaymentRounding(
        decimal originalUnrounded, decimal originalRounding,
        decimal returnedUnrounded, decimal returnedRounding, decimal returnUnrounded) =>
        SalesReturnAmountCalculator.AllocatePaymentRounding(
            originalUnrounded, originalRounding, returnedUnrounded,
            returnedRounding, returnUnrounded);
}
