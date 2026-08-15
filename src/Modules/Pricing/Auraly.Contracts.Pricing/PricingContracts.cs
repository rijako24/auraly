namespace Auraly.Contracts.Pricing;

public static class PricingPermissionCodes
{
    public const string Read = "pricing.read";
    public const string ReadCostBasis = "pricing.cost-basis.read";
    public const string ReviewProposals = "pricing.proposals.review";
    public const string PreparePrices = "pricing.prices.prepare";
    public const string PublishPrices = "pricing.prices.publish";
    public const string BulkPublish = "pricing.bulk-publish";
    public const string ManageRounding = "pricing.rounding.manage";
    public const string ReadHistory = "pricing.history.read";
}

public static class PriceInputModes
{
    public const string Margin = "Margin";
    public const string SalePrice = "SalePrice";
    public static bool IsSupported(string value) => value is Margin or SalePrice;
}

public static class PricingRoundingModes
{
    public const string Nearest = "Nearest";
    public const string Up = "Up";
    public const string Down = "Down";
    public static bool IsSupported(string value) => value is Nearest or Up or Down;
}

public sealed record PricingUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record PriceCalculationRequest(
    decimal CostBasisAmount,
    string InputMode,
    decimal? TargetMarginPercent,
    decimal? SalePrice,
    decimal RoundingIncrement = 1m,
    string RoundingMode = PricingRoundingModes.Nearest,
    decimal SalesTaxRate = 0m);

public sealed record PriceCalculationResult(
    decimal CostBasisAmount,
    string InputMode,
    decimal? TargetMarginPercent,
    decimal UnroundedSalePrice,
    decimal RoundedSalePrice,
    decimal? EffectiveMarginPercent,
    decimal RoundingIncrement,
    string RoundingMode);

public sealed record PriceRevisionQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    string? Status = null,
    Guid? SupplierId = null,
    Guid? SourceDocumentId = null);

public sealed record PriceRevisionListItem(
    Guid ProposalId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid SourceDocumentId,
    int SourceLineNumber,
    string SupplierName,
    decimal? PreviousObservedUnitCost,
    decimal ObservedUnitCost,
    decimal CurrentSalePrice,
    DateTimeOffset? CurrentPricePublishedAt,
    decimal? CurrentMarginPercent,
    decimal? TargetMarginPercent,
    decimal SuggestedSalePrice,
    decimal SalesTaxRate,
    decimal? EffectiveMarginAfterRounding,
    string Status,
    DateTimeOffset CreatedAt,
    string ConcurrencyToken,
    string Origin);

public sealed record PriceRevisionPage(
    IReadOnlyList<PriceRevisionListItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 :
        (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}

public sealed record ReviewPriceProposalRequest(
    string InputMode,
    decimal? TargetMarginPercent,
    decimal? SalePrice,
    decimal RoundingIncrement,
    string RoundingMode,
    string ConcurrencyToken);

public sealed record PublishPriceItem(
    Guid ProposalId,
    string InputMode,
    decimal? TargetMarginPercent,
    decimal? SalePrice,
    decimal RoundingIncrement,
    string RoundingMode,
    string ConcurrencyToken);

public sealed record PublishPricesRequest(IReadOnlyList<PublishPriceItem> Items);

public sealed record ProductPricingContext(
    Guid ProductId,
    string ProductName,
    decimal PreparedSalePrice,
    decimal PublicSalePrice,
    decimal? CostBasisAmount,
    string? CostBasisOrigin,
    decimal? CurrentMarginPercent,
    decimal SalesTaxRate,
    decimal RoundingIncrement,
    string RoundingMode);

public sealed record PublishProductPriceRequest(
    string InputMode,
    decimal? TargetMarginPercent,
    decimal? SalePrice,
    decimal RoundingIncrement = 1m,
    string RoundingMode = PricingRoundingModes.Nearest,
    decimal? CostBasisAmount = null);

public sealed record PreparedProductPrice(
    Guid ProductPriceId,
    Guid ProductId,
    decimal PreparedAmount,
    decimal PublicAmount,
    decimal? CostBasisAmount,
    decimal? EffectiveMarginPercent,
    DateTimeOffset SavedAt);
public sealed record RejectPriceProposalRequest(string ConcurrencyToken, string? Reason);

public sealed record PublishedPrice(
    Guid ProductPriceId,
    Guid ProposalId,
    Guid ProductId,
    decimal Amount,
    decimal? EffectiveMarginPercent,
    long CatalogCursor,
    DateTimeOffset PublishedAt);

public sealed record PublishPricesResult(IReadOnlyList<PublishedPrice> Items, long CatalogCursor);

public sealed record ProductPriceHistoryItem(
    Guid ProductPriceId, Guid ProductId, decimal Amount, string CurrencyCode,
    decimal? CostBasisAmount, decimal? EffectiveMarginPercent, string? InputMode,
    DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, Guid? PublishedByUserId,
    DateTimeOffset? PublishedAt, bool IsActive);
