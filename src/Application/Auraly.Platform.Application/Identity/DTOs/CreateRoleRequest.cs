namespace Auraly.Platform.Application.Identity.DTOs;

public record CreateRoleRequest(
    Guid? TenantId,
    string Name,
    string? Description,
    IReadOnlyList<Guid>? PermissionIds = null);
