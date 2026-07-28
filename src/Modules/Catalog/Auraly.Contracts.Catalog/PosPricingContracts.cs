namespace Auraly.Contracts.Catalog;

public sealed record PosPriceListItem(
    Guid PriceListId, Guid ProductId, decimal MinimumQuantity, decimal Amount, string CurrencyCode);

public sealed record PosPriceChannelItem(
    Guid PriceChannelId, Guid ProductId, decimal Amount, string CurrencyCode, bool IsExcluded);

public sealed record PosCustomerPricing(
    Guid CustomerId, string Identification, string Name, Guid? PriceListId, Guid? PriceChannelId, bool IsActive);

public sealed record PosPricingSnapshot(
    IReadOnlyCollection<PosPriceListItem> PriceListItems,
    IReadOnlyCollection<PosPriceChannelItem> PriceChannelItems,
    IReadOnlyCollection<PosCustomerPricing> Customers);

public sealed record PosResolvedPrice(
    Guid ProductId, decimal BaseAmount, decimal Amount, string CurrencyCode,
    string Source, Guid? PriceListId, Guid? PriceChannelId);
