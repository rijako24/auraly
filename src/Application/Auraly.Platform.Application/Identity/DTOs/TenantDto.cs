namespace Auraly.Platform.Application.Identity.DTOs;

public record TenantDto(
    Guid TenantId,
    string TenantKey,
    string Name,
    string Email,
    bool IsActive,
    DateTime CreatedAt,
    int BusinessCount,
    int MaximumUsers,
    int MaximumEnrolledDevices,
    string InventoryCostBasis,
    bool AllowPromotionChannelCombination,
    int ActiveUserCount,
    int ActiveEnrolledDeviceCount,
    string? LegalName,
    string? Nit,
    string? VerificationDigit,
    string? EntityType,
    string? IdentificationTypeCode,
    string? LogoUrl,
    DateTimeOffset? FiscalCertificateValidTo);

public sealed record FiscalCertificateExpiryAlertDto(
    Guid TenantId,
    string TenantName,
    DateTimeOffset ValidTo,
    bool IsExpired);

public sealed record TenantBrandingDto(
    Guid TenantId,
    string DisplayName,
    string? LegalName,
    string? LogoUrl);
