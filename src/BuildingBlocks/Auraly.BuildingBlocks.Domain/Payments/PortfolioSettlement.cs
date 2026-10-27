namespace Auraly.BuildingBlocks.Domain.Payments;

public sealed record PortfolioAllocation(Guid PositionId, decimal Amount);

// Shared allocation invariant for supplier payments, customer collections and
// the same operations captured in a manual voucher. This performs no effects.
public sealed record PortfolioSettlement(IReadOnlyList<PortfolioAllocation> Allocations, decimal TotalAmount)
{
    public const int MaximumAllocations = 100;

    public static PortfolioSettlement Create(IEnumerable<PortfolioAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        var values = allocations.ToArray();
        if (values.Length is < 1 or > MaximumAllocations)
            throw new ArgumentException($"A settlement must allocate between one and {MaximumAllocations} obligations.", nameof(allocations));
        if (values.Any(item => item.PositionId == Guid.Empty || item.Amount <= 0))
            throw new ArgumentException("Every allocation requires an obligation and a positive amount.", nameof(allocations));
        if (values.Select(item => item.PositionId).Distinct().Count() != values.Length)
            throw new ArgumentException("An obligation can only appear once in a settlement.", nameof(allocations));
        var normalized = values.Select(item => item with { Amount = decimal.Round(item.Amount, 4, MidpointRounding.AwayFromZero) }).ToArray();
        return new(normalized, decimal.Round(normalized.Sum(item => item.Amount), 4, MidpointRounding.AwayFromZero));
    }
}
