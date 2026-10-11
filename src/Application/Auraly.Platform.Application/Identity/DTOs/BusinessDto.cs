namespace Auraly.Platform.Application.Identity.DTOs;

public record BusinessDto(
    Guid BusinessId,
    Guid TenantId,
    string Name,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    string? Website,
    string TimeZone,
    bool SharesProductPrices,
    bool IsActive,
    DateTime CreatedAt,
    string PointOfSaleMode = Auraly.Platform.Domain.Enums.PointOfSaleModes.DirectSale,
    bool RestaurantRequireWaiterCredential = true,
    bool RestaurantRestrictTableToWaiter = false,
    decimal RestaurantSuggestedTipPercent = 0);
