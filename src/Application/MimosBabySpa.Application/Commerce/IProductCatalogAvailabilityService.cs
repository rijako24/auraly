using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Commerce;

public interface IProductCatalogAvailabilityService
{
    ProductSearchResult FilterSellable(ProductSearchResult result);
    bool IsSellable(ProductReference product);
    bool IsSellable(Product product);
    Task<IReadOnlyList<UnavailableOrderItem>> FindUnavailableDraftItemsAsync(
        Guid businessId,
        IReadOnlyList<OrderDraftItem> items,
        CancellationToken ct = default);
}
