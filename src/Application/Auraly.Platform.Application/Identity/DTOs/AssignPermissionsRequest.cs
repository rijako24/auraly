namespace Auraly.Platform.Application.Identity.DTOs;

public record AssignPermissionsRequest(IReadOnlyList<Guid> PermissionIds);
