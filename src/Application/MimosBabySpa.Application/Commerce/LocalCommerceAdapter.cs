using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed class LocalCommerceAdapter : ICommerceAdapter
{
    private readonly IUnitOfWork _unitOfWork;

    public LocalCommerceAdapter(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public CommerceProvider Provider => CommerceProvider.Local;

    public async Task<ProductSearchResult> SearchProductsAsync(ProductSearchRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var products = await _unitOfWork.Products.SearchAsync(ctx.BusinessId, request.Query, request.Category, request.Limit, ct);
        return new ProductSearchResult(
            products.Select(Map).ToList(),
            "local",
            false);
    }

    public async Task<ProductReference?> GetProductAsync(AddOrderItemRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        Product? product = null;
        if (request.ProductId.HasValue)
            product = await _unitOfWork.Products.GetByIdAsync(ctx.BusinessId, request.ProductId.Value, ct);

        if (product is null && !string.IsNullOrWhiteSpace(request.ExternalProductId) && ctx.Connection is not null)
        {
            product = await _unitOfWork.Products.GetByExternalIdAsync(
                ctx.BusinessId,
                ctx.Connection.IntegrationConnectionId,
                request.ExternalProductId,
                ct);
        }

        if (product is null && !string.IsNullOrWhiteSpace(request.Sku))
        {
            var matches = await _unitOfWork.Products.SearchAsync(ctx.BusinessId, request.Sku, null, 1, ct);
            product = matches.FirstOrDefault(p => string.Equals(p.Sku, request.Sku, StringComparison.OrdinalIgnoreCase));
        }

        if (product is null && !string.IsNullOrWhiteSpace(request.Name))
        {
            var matches = await _unitOfWork.Products.SearchAsync(ctx.BusinessId, request.Name, null, 10, ct);
            product = matches.FirstOrDefault(p => string.Equals(p.Name, request.Name, StringComparison.OrdinalIgnoreCase));
        }

        return product is null ? null : Map(product);
    }

    public Task<CreateExternalOrderResult> CreateOrderAsync(Order order, IReadOnlyList<OrderItem> items, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        return Task.FromResult(new CreateExternalOrderResult(order.OrderId.ToString(), null, "local", "{}"));
    }

    private static ProductReference Map(Product product) =>
        new(
            product.ProductId,
            product.ExternalProductId,
            product.Sku,
            product.Name,
            product.Description,
            product.CategoryName,
            product.UnitPrice,
            product.Currency,
            product.StockQuantity,
            !product.ManageStock || (product.StockQuantity ?? 0) > 0,
            product.RawPayloadJson);
}
