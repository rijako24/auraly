namespace Auraly.Domain.Inventory;

// Quantity provenance only. Valuation and physical availability keep their
// existing owners; these portions never supply a stock balance or a unit cost.
public sealed record InventoryOriginPortion(
    Guid OriginId, Guid? SourceMovementId, DateTimeOffset RecordedAt, decimal Quantity,
    int SourceLineNumber = 0, long SourceProcessingSequence = 0);

public sealed record InventoryOriginAttributionResult(
    IReadOnlyList<InventoryOriginPortion> Available,
    IReadOnlyList<InventoryOriginPortion> Movement);

public static class InventoryOriginAttribution
{
    public static InventoryOriginAttributionResult Apply(
        decimal quantityBefore, decimal quantityChange,
        IReadOnlyList<InventoryOriginPortion> available,
        IReadOnlyList<InventoryOriginPortion>? incoming,
        InventoryOriginPortion unknown)
    {
        if (decimal.Round(quantityBefore,6)!=quantityBefore || decimal.Round(quantityChange,6)!=quantityChange)
            throw new ArgumentException("Origin attribution requires quantities at inventory precision.");
        Validate(available);
        if (available.Sum(portion => portion.Quantity) != Math.Max(quantityBefore, 0m))
            throw new InvalidOperationException("Inventory origin detail does not match the locked warehouse quantity.");
        if (quantityChange == 0m) return new(available, []);
        var remaining = available.ToDictionary(portion => portion.OriginId);
        var movement = new List<InventoryOriginPortion>();
        if (quantityChange < 0m)
        {
            if (incoming is not null)
                throw new ArgumentException("An inventory issue cannot receive origin portions.", nameof(incoming));
            var demand = -quantityChange;
            foreach (var portion in OldestFirst(available))
            {
                var taken = Math.Min(portion.Quantity, demand);
                if (taken == 0m) break;
                movement.Add(portion with { Quantity = -taken });
                if (taken == portion.Quantity) remaining.Remove(portion.OriginId);
                else remaining[portion.OriginId] = portion with { Quantity = portion.Quantity - taken };
                demand -= taken;
            }
            // An accepted issue may cross zero. Its unbacked quantity has no
            // receipt origin; a future receipt must not acquire that history.
            if (demand > 0m)
            {
                Validate([unknown with { Quantity=demand }]);
                movement.Add(unknown with { Quantity = -demand });
            }
        }
        else
        {
            if (incoming is null)
                throw new ArgumentException("An inventory receipt requires origin portions.", nameof(incoming));
            Validate(incoming);
            if (incoming.Sum(portion => portion.Quantity) != quantityChange)
                throw new InvalidOperationException("Incoming origin quantities do not match the physical receipt.");
            var coveredNegative = Math.Min(quantityChange, Math.Max(-quantityBefore, 0m));
            foreach (var portion in OldestFirst(incoming))
            {
                movement.Add(portion);
                var covered = Math.Min(coveredNegative, portion.Quantity);
                coveredNegative -= covered;
                var retained = portion.Quantity - covered;
                if (retained == 0m) continue;
                if (remaining.TryGetValue(portion.OriginId, out var previous))
                {
                    if (previous.SourceMovementId != portion.SourceMovementId || previous.RecordedAt != portion.RecordedAt ||
                        previous.SourceLineNumber != portion.SourceLineNumber ||
                        previous.SourceProcessingSequence != portion.SourceProcessingSequence)
                        throw new InvalidOperationException("One inventory origin cannot have conflicting source facts.");
                    remaining[portion.OriginId] = previous with { Quantity = previous.Quantity + retained };
                }
                else remaining.Add(portion.OriginId, portion with { Quantity = retained });
            }
        }
        if (remaining.Values.Sum(portion => portion.Quantity) != Math.Max(quantityBefore + quantityChange, 0m))
            throw new InvalidOperationException("Inventory attribution did not preserve the physical quantity.");
        return new(OldestFirst(remaining.Values).ToArray(), movement);
    }

    public static IOrderedEnumerable<InventoryOriginPortion> OldestFirst(IEnumerable<InventoryOriginPortion> portions) =>
        portions.OrderBy(portion => portion.SourceMovementId is null ? 0 : 1)
            .ThenBy(portion => portion.RecordedAt).ThenBy(portion => portion.SourceProcessingSequence)
            .ThenBy(portion => portion.SourceLineNumber).ThenBy(portion => portion.OriginId);

    private static void Validate(IReadOnlyList<InventoryOriginPortion> portions)
    {
        if (portions.Any(portion => portion.OriginId == Guid.Empty || portion.SourceMovementId == Guid.Empty ||
                portion.Quantity <= 0m || decimal.Round(portion.Quantity, 6) != portion.Quantity) ||
            portions.Select(portion => portion.OriginId).Distinct().Count() != portions.Count)
            throw new ArgumentException("Inventory origin portions require distinct identities and positive quantities at inventory precision.");
    }
}
