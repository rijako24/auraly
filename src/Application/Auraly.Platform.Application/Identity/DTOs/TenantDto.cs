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
    int ActiveUserCount,
    int ActiveEnrolledDeviceCount,
    string? LegalName,
    string? Nit,
    string? VerificationDigit);
