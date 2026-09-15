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
        Guid? partySiteId,
        CancellationToken cancellationToken = default)
    {
        if (customerId.HasValue != partySiteId.HasValue)
            throw new ArgumentException("Customer and site must be selected together.");
        var customer = customerId is null
            ? null
            : await catalog.GetCustomerAsync(customerId.Value, cancellationToken)
              ?? throw new KeyNotFoundException("The customer is not available in the local POS catalog.");
        PosCustomerSite? site = null;
        if (customer is not null)
        {
            site = customer.Sites?.SingleOrDefault(
                value => value.PartySiteId == partySiteId!.Value);
            if (site is null)
                throw new KeyNotFoundException(
                    "The customer site is not available in the local POS catalog.");
            customer = customer with
            {
                PartySiteId = site.PartySiteId,
                SiteName = site.Name,
                SiteAddress = site.AddressLine
            };
        }
        var updated = await pricing.RepriceAsync(
            draftId, customer?.CustomerId, site?.PartySiteId, cancellationToken);
        return new PosCustomerSelection(updated, customer);
    }
}
