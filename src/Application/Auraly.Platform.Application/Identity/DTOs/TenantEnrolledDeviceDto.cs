namespace Auraly.Platform.Application.Identity.DTOs;

public record TenantEnrolledDeviceDto(
    Guid DeviceId,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    Guid? BusinessId,
    string? BusinessName);