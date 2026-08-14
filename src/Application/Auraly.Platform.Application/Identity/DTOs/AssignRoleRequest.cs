namespace Auraly.Platform.Application.Identity.DTOs;

public record AssignRoleRequest(Guid RoleId, Guid? BusinessId = null);
