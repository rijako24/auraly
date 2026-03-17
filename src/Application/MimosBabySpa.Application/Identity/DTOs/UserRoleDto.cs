namespace MimosBabySpa.Application.Identity.DTOs;

public record UserRoleDto(
    Guid RoleId,
    string RoleName,
    Guid? BusinessId,
    string? BusinessName,
    DateTime AssignedAt);
