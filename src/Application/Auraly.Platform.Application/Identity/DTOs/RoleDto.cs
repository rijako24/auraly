namespace Auraly.Platform.Application.Identity.DTOs;

public record RoleDto(
    Guid RoleId,
    Guid? TenantId,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive,
    DateTime CreatedAt,
    int UserCount,
    int PermissionCount);
