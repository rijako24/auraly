namespace Auraly.Domain.Receivables;

public sealed record ReceivableAllocation(Guid ReceivableId, decimal Amount);

public sealed record ReceivableSettlement(
    IReadOnlyList<ReceivableAllocation> Allocations,
    decimal TotalAmount)
{
    public static ReceivableSettlement Create(IEnumerable<ReceivableAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        var values = allocations.ToArray();
        if (values.Length is < 1 or > 100)
            throw new ArgumentException("A receipt must allocate between one and 100 receivables.", nameof(allocations));
        if (values.Any(value => value.ReceivableId == Guid.Empty || value.Amount <= 0))
            throw new ArgumentException("Every allocation requires a receivable and a positive amount.", nameof(allocations));
        if (values.Select(value => value.ReceivableId).Distinct().Count() != values.Length)
            throw new ArgumentException("A receivable can only appear once in a receipt.", nameof(allocations));

        var normalized = values.Select(value => value with
        {
            Amount = decimal.Round(value.Amount, 4, MidpointRounding.AwayFromZero)
        }).ToArray();
        return new(normalized, decimal.Round(normalized.Sum(value => value.Amount), 4));
    }
}
