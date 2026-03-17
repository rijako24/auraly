namespace MimosBabySpa.Application.Identity.DTOs;

public record CreateRoleRequest(
    Guid? TenantId,
    string Name,
    string? Description);
