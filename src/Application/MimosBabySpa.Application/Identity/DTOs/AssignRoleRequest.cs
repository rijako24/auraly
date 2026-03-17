namespace MimosBabySpa.Application.Identity.DTOs;

public record AssignRoleRequest(Guid RoleId, Guid? BusinessId = null);
