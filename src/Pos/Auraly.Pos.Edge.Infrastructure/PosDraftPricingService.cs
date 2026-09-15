using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Domain.Money;

namespace Auraly.Pos.Edge.Infrastructure;

/// <summary>Mechanical draft repricing adapter; all pricing decisions belong to PromotionPriceResolver.</summary>
public sealed class PosDraftPricingService(PosCatalogStore catalog, PosDraftStore drafts)
{
    public async Task<PosDraft> RepriceAsync(
        DraftId draftId, Guid? customerId, Guid? partySiteId, CancellationToken ct = default)
    {
        var draft = await drafts.GetAsync(draftId, ct)
            ?? throw new KeyNotFoundException("The draft does not exist.");
        var prices = await catalog.ResolvePricesAsync(
            draft.Lines.Select(line => new PosPriceLineRequest(
                line.LineId.ToString("D"), line.ProductId.Value, line.Quantity,
                !line.IsPriceOverridden)).ToArray(),
            customerId,
            ct);
        return await drafts.AssignCustomerAndPricesAsync(
            draftId,
            customerId,
            partySiteId,
            draft.Lines.Select(line =>
            {
                var price = prices[line.LineId.ToString("D")];
                if (line.IsPriceOverridden)
                    return new PosDraftLinePriceUpdate(
                        line.LineId, line.BaseUnitPrice, line.UnitPrice, line.CurrencyCode,
                        line.PriceSource, line.PriceChannelId, line.PromotionDiscount);
                return new PosDraftLinePriceUpdate(
                    line.LineId,
                    MonetaryRounding.CeilingLineUnitPrice(price.BaseAmount),
                    MonetaryRounding.CeilingLineUnitPrice(price.ReferenceAmount ?? price.Amount),
                    price.CurrencyCode,
                    price.Source, price.PriceChannelId, price.PromotionDiscount);
            }).ToArray(),
            ct);
    }
}
