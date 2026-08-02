namespace Auraly.Domain.Payables;

public sealed record PayableAllocation(Guid PayableId, decimal Amount);

public sealed record PayableSettlement(
    IReadOnlyList<PayableAllocation> Allocations,
    decimal TotalAmount)
{
    public static PayableSettlement Create(IEnumerable<PayableAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        var values = allocations.ToArray();
        if (values.Length == 0)
            throw new ArgumentException("At least one payable allocation is required.", nameof(allocations));
        if (values.Length > 100)
            throw new ArgumentException("A payment cannot settle more than 100 obligations.", nameof(allocations));
        if (values.Any(item => item.PayableId == Guid.Empty))
            throw new ArgumentException("Every allocation requires a PayableId.", nameof(allocations));
        if (values.Any(item => item.Amount <= 0))
            throw new ArgumentException("Every allocation amount must be greater than zero.", nameof(allocations));
        if (values.Select(item => item.PayableId).Distinct().Count() != values.Length)
            throw new ArgumentException("A payable can only appear once in a payment.", nameof(allocations));

        var normalized = values
            .Select(item => item with
            {
                Amount = decimal.Round(item.Amount, 4, MidpointRounding.AwayFromZero)
            })
            .ToArray();
        var total = decimal.Round(
            normalized.Sum(item => item.Amount), 4, MidpointRounding.AwayFromZero);
        if (total <= 0)
            throw new ArgumentException("The payment total must be greater than zero.", nameof(allocations));
        return new PayableSettlement(normalized, total);
    }
}
