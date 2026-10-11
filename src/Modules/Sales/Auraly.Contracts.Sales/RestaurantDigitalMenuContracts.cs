namespace Auraly.Contracts.Sales;

public sealed record RestaurantDigitalMenuConfiguration(
    string PublicCode, string Title, string? Introduction,
    bool IsPublished, string RowVersion);

public sealed record SaveRestaurantDigitalMenuRequest(
    string Title, string? Introduction, bool IsPublished,
    string? RowVersion);

public sealed record RestaurantDigitalMenuCategory(
    Guid CategoryId, string Name, int ProductCount);

public sealed record RestaurantDigitalMenuView(
    string Title, string? Introduction,
    IReadOnlyList<RestaurantDigitalMenuCategory> Categories);

public sealed record RestaurantDigitalMenuProduct(
    Guid ProductId, string Name, string? Description,
    decimal Price, string CurrencyCode, string? ImageUrl);

public sealed record RestaurantDigitalMenuProductPage(
    IReadOnlyList<RestaurantDigitalMenuProduct> Items,
    bool HasMore);
