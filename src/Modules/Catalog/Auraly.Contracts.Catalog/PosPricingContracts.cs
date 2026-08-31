namespace Auraly.Contracts.Catalog;

public sealed record PosPriceChannelItem(
    Guid PriceChannelId, Guid ProductId, decimal MinimumQuantity, decimal Amount, string CurrencyCode, bool IsExcluded);

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

public sealed record PosPricingSnapshot(
    IReadOnlyCollection<PosPriceChannelItem> PriceChannelItems,
    IReadOnlyCollection<PosCustomerPricing> Customers,
    IReadOnlyCollection<PosWithholdingRule>? WithholdingRules = null,
    bool? WarehouseAllowsNegativeStock = null);

public sealed record PosResolvedPrice(
    Guid ProductId, decimal BaseAmount, decimal Amount, string CurrencyCode,
    string Source, Guid? PriceChannelId);
