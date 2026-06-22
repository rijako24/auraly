namespace MimosBabySpa.Application.Commerce;

public sealed record ProductSearchRequest(
    string? Query,
    string? Category,
    int Limit = 10,
    bool IncludeStock = true);

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
    bool IsAvailable,
    decimal? EffectiveUnitPrice = null,
    decimal? DiscountAmount = null,
    string? PromotionName = null,
    string? PromotionSummary = null,
    string? RawPayloadJson = null);

public sealed record ProductSearchResult(
    IReadOnlyList<ProductReference> Products,
    string Source,
    bool HasMore = false);
