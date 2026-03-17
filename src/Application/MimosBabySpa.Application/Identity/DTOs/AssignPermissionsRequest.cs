namespace MimosBabySpa.Application.Identity.DTOs;

public record AssignPermissionsRequest(IReadOnlyList<Guid> PermissionIds);
