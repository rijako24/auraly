using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed class LocalCommerceAdapter : ICommerceAdapter
{
    private static readonly string Source = CommerceProvider.Local.ToString().ToLowerInvariant();
    private readonly IUnitOfWork _unitOfWork;

    public LocalCommerceAdapter(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public CommerceProvider Provider => CommerceProvider.Local;

    public async Task<ProductSearchResult> SearchProductsAsync(ProductSearchRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var repositoryLimit = HasStructuredFilters(request) ? 50 : request.Limit;
        var products = await _unitOfWork.Products.SearchAsync(
            ctx.BusinessId,
            request.Query,
            request.Category,
            repositoryLimit,
            ct,
            includeInactive: true);
        var filtered = products
            .Select(Map)
            .Where(product => ProductMatches(product, request))
            .Take(Math.Clamp(request.Limit, 1, 50))
            .ToList();
        return new ProductSearchResult(
            filtered,
            Source,
            false,
            ProductSearchAppliedFilters.From(request));
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
        return Task.FromResult(new CreateExternalOrderResult(order.OrderId.ToString(), null, Source, "{}"));
    }

    private static bool HasStructuredFilters(ProductSearchRequest request) =>
        !string.IsNullOrWhiteSpace(request.Family)
        || !string.IsNullOrWhiteSpace(request.Subcategory)
        || !string.IsNullOrWhiteSpace(request.ProductClass);

    private static bool ProductMatches(ProductReference product, ProductSearchRequest request) =>
        MatchesFilter(product.CategoryName, request.Category)
        && MatchesFilter(CombinedMetadata(product), request.Family)
        && MatchesFilter(CombinedMetadata(product), request.Subcategory)
        && MatchesFilter(CombinedMetadata(product), request.ProductClass);

    private static string CombinedMetadata(ProductReference product) =>
        string.Join(" ", new[]
        {
            product.CategoryName,
            product.FamilyName,
            product.SubcategoryName,
            product.ProductClassName,
            product.RawPayloadJson
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static bool MatchesFilter(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter)
        || (!string.IsNullOrWhiteSpace(value) && value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));

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
            null,
            null,
            null,
            null,
            product.RawPayloadJson)
        { IsActive = product.IsActive };
}
