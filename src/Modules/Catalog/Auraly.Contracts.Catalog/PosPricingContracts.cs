namespace Auraly.Contracts.Catalog;

public sealed record PosPriceChannelItem(
    Guid PriceChannelId, Guid ProductId, decimal MinimumQuantity, decimal Amount, string CurrencyCode, bool IsExcluded);

public sealed record PosCustomerPricing(
    Guid CustomerId, string Identification, string Name, Guid? PriceChannelId,
    bool IsActive, bool RequiresElectronicInvoice = false);

public sealed record PosPricingSnapshot(
    IReadOnlyCollection<PosPriceChannelItem> PriceChannelItems,
    IReadOnlyCollection<PosCustomerPricing> Customers);

public sealed record PosResolvedPrice(
    Guid ProductId, decimal BaseAmount, decimal Amount, string CurrencyCode,
    string Source, Guid? PriceChannelId);
