namespace Auraly.Platform.Domain.Enums;

public static class PointOfSaleModes
{
    public const string CatalogCode = "point-of-sale-mode";
    public const string DirectSale = "DirectSale";
    public const string Store = "Store";
    public const string RestaurantBar = "RestaurantBar";

    public static bool IsSupported(string code) => code is DirectSale or Store or RestaurantBar;
}
