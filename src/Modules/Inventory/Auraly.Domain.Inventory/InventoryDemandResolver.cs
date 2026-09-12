namespace Auraly.Domain.Inventory;

public sealed record InventoryDemandLine(
    Guid LineId,
    Guid ProductId,
    Guid InventoryProductId,
    decimal InventoryFactor,
    decimal Quantity,
    bool ManagesStock)
{
    public decimal RequiredInventoryQuantity => Quantity * InventoryFactor;
}

public sealed record InventoryDemand(
    Guid InventoryProductId,
    decimal RequiredInventoryQuantity,
    IReadOnlyList<InventoryDemandLine> Lines);

public sealed record InventoryDemandAllocation(
    InventoryDemandLine Line,
    decimal AvailableInventoryQuantity,
    decimal RequiredInventoryQuantity,
    bool CanReserve);

/// <summary>
/// Canonical arithmetic for products that consume their own inventory or a
/// linked parent's inventory. Data access supplies facts; every sales and order
/// runtime groups and converts them here.
/// </summary>
public static class InventoryDemandResolver
{
    public static IReadOnlyList<InventoryDemand> Resolve(
        IEnumerable<InventoryDemandLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var values = lines.ToArray();
        if (values.Any(line => line.Quantity <= 0))
            throw new ArgumentOutOfRangeException(nameof(lines), "Inventory quantities must be positive.");
        if (values.Any(line => line.InventoryFactor <= 0))
            throw new ArgumentOutOfRangeException(nameof(lines), "Inventory factors must be positive.");

        return values
            .Where(line => line.ManagesStock)
            .GroupBy(line => line.InventoryProductId)
            .Select(group => new InventoryDemand(
                group.Key,
                group.Sum(line => line.RequiredInventoryQuantity),
                group.ToArray()))
            .ToArray();
    }

    public static decimal InProductUnits(
        decimal inventoryQuantity,
        decimal inventoryFactor)
    {
        if (inventoryFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(inventoryFactor));
        return inventoryQuantity / inventoryFactor;
    }

    public static IReadOnlyList<InventoryDemandAllocation> AllocateWholeLines(
        IEnumerable<InventoryDemandLine> lines,
        IReadOnlyDictionary<Guid, decimal> availableByInventoryProduct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(availableByInventoryProduct);
        var values = lines.ToArray();
        _ = Resolve(values);
        var remaining = availableByInventoryProduct.ToDictionary(pair => pair.Key, pair => pair.Value);
        var result = new List<InventoryDemandAllocation>(values.Length);
        foreach (var line in values)
        {
            if (!line.ManagesStock)
            {
                result.Add(new(line, 0m, 0m, true));
                continue;
            }
            var available = remaining.GetValueOrDefault(line.InventoryProductId);
            var required = line.RequiredInventoryQuantity;
            var canReserve = available >= required;
            result.Add(new(line, available, required, canReserve));
            if (canReserve) remaining[line.InventoryProductId] = available - required;
        }
        return result;
    }
}
