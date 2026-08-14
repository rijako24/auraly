using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Commerce;

public sealed class ProductCatalogAvailabilityService : IProductCatalogAvailabilityService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductCatalogAvailabilityService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public ProductSearchResult FilterSellable(ProductSearchResult result) =>
        result with { Products = result.Products.Where(IsSellable).ToList() };


    public bool IsSellable(ProductReference product) =>
        product.IsActive && (!product.StockQuantity.HasValue || product.StockQuantity.Value > 0);

    public bool IsSellable(Product product) =>
        product.IsActive && (!product.ManageStock || (product.StockQuantity ?? 0) > 0);

    public async Task<IReadOnlyList<UnavailableOrderItem>> FindUnavailableDraftItemsAsync(
        Guid businessId,
        IReadOnlyList<OrderDraftItem> items,
        CancellationToken ct = default)
    {
        var products = new Dictionary<Guid, Product>();
        foreach (var productId in items.Select(i => i.ProductId).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            var product = await _unitOfWork.Products.GetByIdAsync(businessId, productId, ct);
            if (product is not null)
                products[productId] = product;
        }

        var unavailable = new List<UnavailableOrderItem>();
        foreach (var item in items.Where(i => i.ProductId.HasValue))
        {
            if (!products.TryGetValue(item.ProductId!.Value, out var product))
            {
                unavailable.Add(ToUnavailable(item, "not_found"));
                continue;
            }

            if (!IsSellable(product))
                unavailable.Add(ToUnavailable(item, product.IsActive ? "unavailable" : "inactive"));
        }

        return unavailable;
    }

    private static UnavailableOrderItem ToUnavailable(OrderDraftItem item, string reason) =>
        new(
            item.OrderDraftItemId,
            item.ProductId,
            item.Sku,
            item.ProductNameSnapshot,
            reason);
}
