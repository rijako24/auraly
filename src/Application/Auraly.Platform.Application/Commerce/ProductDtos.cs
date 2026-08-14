namespace Auraly.Platform.Application.Commerce;

public enum ProductCatalogQueryMode
{
    SearchTarget = 0,
    ExploreCatalog = 1,
    ContinueResults = 2
}

public sealed record ProductLookupRequest(
    Guid? ProductId,
    string? ExternalProductId,
    string? Sku,
    string? Name,
    string? SearchText = null);

public sealed record ProductSearchRequest(
    string? Query,
    string? Category,
    int Limit = 10,
    bool IncludeStock = true,
    string? Family = null,
    string? Subcategory = null,
    string? ProductClass = null,
    int Page = 1,
    ProductCatalogQueryMode Mode = ProductCatalogQueryMode.SearchTarget);

public sealed record ProductReference(
    Guid? ProductId,
    string? ExternalProductId,
    string? Sku,
    string Name,
    string? Description,
    string? CategoryName,
    decimal UnitPrice,
    string Currency,
    decimal? StockQuantity,
    decimal? EffectiveUnitPrice = null,
    decimal? DiscountAmount = null,
    string? PromotionName = null,
    string? PromotionSummary = null,
    string? RawPayloadJson = null,
    string? FamilyName = null,
    string? SubcategoryName = null,
    string? ProductClassName = null,
    string? ExternalCategoryId = null,
    Guid? IntegrationConnectionId = null)
{
    public bool IsActive { get; init; } = true;
}

public sealed record ProductCategoryReference(
    Guid? ProductCategoryId,
    string? ExternalCategoryId,
    string Name,
    int DisplayOrder);

public sealed record ProductCategoryPage(
    IReadOnlyList<ProductCategoryReference> Categories,
    bool HasMore,
    int Page,
    int PageSize,
    bool CatalogReady = true);

public sealed record ProductSearchResult(
    IReadOnlyList<ProductReference> Products,
    string Source,
    bool HasMore = false,
    ProductSearchAppliedFilters? AppliedFilters = null,
    bool CatalogReady = true);

public sealed record ProductSearchAppliedFilters(
    string? Query,
    string? Category,
    string? Family,
    string? Subcategory,
    string? ProductClass,
    int Limit,
    int Page)
{
    public static ProductSearchAppliedFilters From(ProductSearchRequest request) =>
        new(
            request.Query,
            request.Category,
            request.Family,
            request.Subcategory,
            request.ProductClass,
            request.Limit,
            request.Page);
}
