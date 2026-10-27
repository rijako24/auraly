using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Domain.Payables;

public sealed record PayableAllocation(Guid PayableId, decimal Amount);

public sealed record PayableSettlement(
    IReadOnlyList<PayableAllocation> Allocations,
    decimal TotalAmount)
{
    public static PayableSettlement Create(IEnumerable<PayableAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        var settlement = PortfolioSettlement.Create(allocations.Select(item => new PortfolioAllocation(item.PayableId, item.Amount)));
        if (settlement.TotalAmount <= 0)
            throw new ArgumentException("The payment total must be greater than zero.", nameof(allocations));
        return new(settlement.Allocations.Select(item => new PayableAllocation(item.PositionId, item.Amount)).ToArray(), settlement.TotalAmount);
    }
}
