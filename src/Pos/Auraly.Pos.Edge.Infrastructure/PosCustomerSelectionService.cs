using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosCustomerSelection(
    PosDraft Draft,
    PosCustomerPricing? Customer);

public sealed class PosCustomerSelectionService(
    PosCatalogStore catalog,
    PosDraftStore drafts)
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
        var current = await drafts.GetAsync(draftId, cancellationToken)
            ?? throw new KeyNotFoundException("The draft does not exist.");
        var prices = new List<PosDraftLinePriceUpdate>(current.Lines.Count);
        foreach (var line in current.Lines)
        {
            var price = await catalog.ResolvePriceAsync(
                line.ProductId.Value,
                customer?.CustomerId,
                line.Quantity,
                cancellationToken);
            prices.Add(new PosDraftLinePriceUpdate(
                line.LineId,
                price.BaseAmount,
                price.Amount,
                price.CurrencyCode,
                price.Source,
                price.PriceListId,
                price.PriceChannelId));
        }
        var updated = await drafts.AssignCustomerAndPricesAsync(
            draftId,
            customer?.CustomerId,
            prices,
            cancellationToken);
        return new PosCustomerSelection(updated, customer);
    }
}
