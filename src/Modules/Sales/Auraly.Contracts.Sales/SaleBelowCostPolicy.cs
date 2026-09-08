namespace Auraly.Contracts.Sales;

public static class SaleBelowCostPolicy
{
    public static bool IsBelowCost(decimal quantity, decimal taxExclusiveNet, decimal unitCost) =>
        quantity > 0m && unitCost > 0m && taxExclusiveNet / quantity < unitCost;
}
