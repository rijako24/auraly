namespace MimosBabySpa.Application.Identity.DTOs;

public sealed record ProductOfferDto(
    Guid ProductOfferId,
    Guid ProductId,
    string Condition,
    int? StorageGb,
    string? Color,
    string? VariantLabel,
    decimal UnitPrice,
    string Currency,
    int? MinimumBatteryHealthPercent,
    bool IsAvailable,
    bool IsActive,
    string? PriceSourceUrl,
    DateTime? PriceObservedAtUtc,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record SaveProductOfferRequest(
    string Condition,
    int? StorageGb,
    string? Color,
    string? VariantLabel,
    decimal UnitPrice,
    string Currency,
    int? MinimumBatteryHealthPercent,
    bool IsAvailable,
    bool IsActive,
    string? PriceSourceUrl,
    DateTime? PriceObservedAtUtc);

public sealed record ProductImageDto(
    Guid ProductImageId,
    Guid ProductId,
    Guid? ProductOfferId,
    string MediaUrl,
    string? AltText,
    int DisplayOrder,
    bool IsPrimary,
    bool IsActive);

public sealed record AddProductImageUrlRequest(
    Guid? ProductOfferId,
    string MediaUrl,
    string? AltText,
    int DisplayOrder,
    bool IsPrimary);
