using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Domain.Receivables;

public sealed record ReceivableAllocation(Guid ReceivableId, decimal Amount);

public sealed record ReceivableSettlement(
    IReadOnlyList<ReceivableAllocation> Allocations,
    decimal TotalAmount)
{
    public static ReceivableSettlement Create(IEnumerable<ReceivableAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        var settlement = PortfolioSettlement.Create(allocations.Select(item => new PortfolioAllocation(item.ReceivableId, item.Amount)));
        return new(settlement.Allocations.Select(item => new ReceivableAllocation(item.PositionId, item.Amount)).ToArray(), settlement.TotalAmount);
    }
}
