namespace Auraly.Contracts.Catalog;

public sealed record PosPriceChannelDefinition(
    Guid PriceChannelId, string Code, string Name, string Strategy, decimal? Value);

public sealed record PosPriceChannelTier(
    Guid PriceChannelId, Guid ProductId, decimal MinimumQuantity, decimal Amount, string CurrencyCode);

public sealed record PosPriceChannelExclusion(
    Guid PriceChannelId, string ScopeType, Guid? ProductId,
    Guid? ProductCategoryId, Guid? ProductBrandId);

public sealed record PosCustomerPricing(
    Guid CustomerId, string Identification, string Name, Guid? PriceChannelId,
    bool IsActive, bool RequiresElectronicInvoice = false,
    bool AppliesWithholding = false,
    IReadOnlyList<string>? TaxResponsibilities = null,
    string? TaxJurisdictionCode = null);

public sealed record PosWithholdingRule(
    Guid RuleId, int Version, string Code, string Name, string Kind,
    string Direction, string Moment, string BaseKind, string? ConceptCode,
    string? JurisdictionCode, decimal Rate, decimal MinimumBase,
    IReadOnlyList<string> RequiredResponsibilities, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, bool IsActive);

public sealed record PosPromotionCondition(
    int ItemType, Guid? ProductId, Guid? ServiceId, string? CategoryName,
    decimal MinimumQuantity, decimal? MinimumSubtotal);

public sealed record PosPromotionBenefit(
    int BenefitType, int TargetItemType, Guid? ProductId, Guid? ServiceId,
    string? CategoryName, decimal? DiscountPercentage, decimal? DiscountAmount,
    decimal? FixedUnitPrice, decimal? AppliesToQuantity);

public sealed record PosPromotion(
    Guid PromotionId, string Name, int Priority, bool IsCombinable,
    string? CouponCode, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc,
    DateTimeOffset CreatedAtUtc, IReadOnlyCollection<PosPromotionCondition> Conditions,
    IReadOnlyCollection<PosPromotionBenefit> Benefits);

public sealed record PosPricingSnapshot(
    IReadOnlyCollection<PosPriceChannelDefinition> PriceChannels,
    IReadOnlyCollection<PosPriceChannelTier> PriceChannelTiers,
    IReadOnlyCollection<PosPriceChannelExclusion> PriceChannelExclusions,
    IReadOnlyCollection<PosCustomerPricing> Customers,
    IReadOnlyCollection<PosWithholdingRule>? WithholdingRules = null,
    bool? WarehouseAllowsNegativeStock = null,
    bool AllowPromotionChannelCombination = false,
    IReadOnlyCollection<PosPromotion>? Promotions = null);

public sealed record PosResolvedPrice(
    Guid ProductId, decimal BaseAmount, decimal Amount, string CurrencyCode,
    string Source, Guid? PriceChannelId,
    decimal PromotionDiscount = 0,
    IReadOnlyCollection<Guid>? PromotionIds = null,
    decimal? ReferenceAmount = null);
