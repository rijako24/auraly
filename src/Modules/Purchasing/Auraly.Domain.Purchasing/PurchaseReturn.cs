namespace Auraly.Domain.Purchasing;

public sealed record PurchaseReturnAllocation(
    decimal Quantity, decimal DiscountAmount, decimal NetAmount,
    decimal TaxAmount, decimal LineTotal);

public static class PurchaseReturnCalculator
{
    public static PurchaseReturnAllocation Allocate(
        decimal receivedQuantity, decimal previouslyReturnedQuantity,
        decimal requestedQuantity, decimal originalDiscount,
        decimal previouslyReturnedDiscount, decimal originalNet,
        decimal previouslyReturnedNet, decimal originalTax,
        decimal previouslyReturnedTax, decimal originalTotal,
        decimal previouslyReturnedTotal)
    {
        if (receivedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(receivedQuantity));
        if (previouslyReturnedQuantity < 0 || previouslyReturnedQuantity > receivedQuantity)
            throw new ArgumentOutOfRangeException(nameof(previouslyReturnedQuantity));
        var available = receivedQuantity - previouslyReturnedQuantity;
        if (requestedQuantity <= 0 || requestedQuantity > available)
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity));

        var isRemainder = requestedQuantity == available;
        decimal Amount(decimal original, decimal returned) => isRemainder
            ? decimal.Round(original - returned, 4, MidpointRounding.AwayFromZero)
            : decimal.Round(original * requestedQuantity / receivedQuantity, 4,
                MidpointRounding.AwayFromZero);
        var discount = Amount(originalDiscount, previouslyReturnedDiscount);
        var net = Amount(originalNet, previouslyReturnedNet);
        var tax = Amount(originalTax, previouslyReturnedTax);
        var total = Amount(originalTotal, previouslyReturnedTotal);
        if (discount < 0 || net < 0 || tax < 0 || total <= 0 || total != net + tax)
            throw new InvalidOperationException(
                "The purchase return allocation does not reconcile with the original receipt.");
        return new PurchaseReturnAllocation(requestedQuantity, discount, net, tax, total);
    }
}