using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosCustomerSelection(
    PosDraft Draft,
    PosCustomerPricing? Customer);

public sealed class PosCustomerSelectionService(
    PosCatalogStore catalog,
    PosDraftPricingService pricing)
{
    public async Task<PosCustomerSelection> SelectAsync(
        DraftId draftId,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = customerId is null
            ? null
            : await catalog.GetCustomerAsync(customerId.Value, cancellationToken)
              ?? throw new KeyNotFoundException("The customer is not available in the local POS catalog.");
        var updated = await pricing.RepriceAsync(draftId, customer?.CustomerId, cancellationToken);
        return new PosCustomerSelection(updated, customer);
    }
}
