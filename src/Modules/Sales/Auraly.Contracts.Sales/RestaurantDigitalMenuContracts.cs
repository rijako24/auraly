namespace Auraly.Contracts.Sales;

public sealed record RestaurantDigitalMenuConfiguration(
    string PublicCode, string Title, string? Introduction,
    bool IsPublished, string RowVersion,
    IReadOnlyList<RestaurantDigitalMenuSelectedProduct> Products);

public sealed record RestaurantDigitalMenuSelectedProduct(
    Guid ProductId, string ProductName, string ProductCode,
    string? PublicDescription);

public sealed record RestaurantDigitalMenuSelectedProductInput(
    Guid ProductId, string? PublicDescription);

public sealed record SaveRestaurantDigitalMenuRequest(
    string Title, string? Introduction, bool IsPublished,
    string? RowVersion,
    IReadOnlyList<RestaurantDigitalMenuSelectedProductInput>? Products = null);

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
