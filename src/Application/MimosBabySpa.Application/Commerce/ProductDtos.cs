namespace MimosBabySpa.Application.Commerce;

public sealed record ProductSearchRequest(
    string? Query,
    string? Category,
    int Limit = 10,
    bool IncludeStock = true,
    string? Family = null,
    string? Subcategory = null,
    string? ProductClass = null,
    int Page = 1);

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
    string? ProductClassName = null)
{
    public bool IsActive { get; init; } = true;
}

public sealed record ProductSearchResult(
    IReadOnlyList<ProductReference> Products,
    string Source,
    bool HasMore = false,
    ProductSearchAppliedFilters? AppliedFilters = null);

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
