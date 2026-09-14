namespace Auraly.Platform.Application.Identity.DTOs;

public sealed record RolePermissionWorkspaceDto(
    RoleDto Role,
    IReadOnlyList<PermissionDto> Permissions,
    IReadOnlyList<Guid> AssignedPermissionIds);
