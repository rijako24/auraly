namespace Auraly.Platform.Application.Identity.DTOs;

public record PermissionDto(
    Guid PermissionId,
    string Module,
    string Action,
    string Resource,
    string? Description);
