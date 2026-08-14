namespace MimosBabySpa.Application.Identity.DTOs;

public record TenantDto(
    Guid TenantId,
    string TenantKey,
    string Name,
    string Email,
    bool IsActive,
    DateTime CreatedAt,
    int BusinessCount);
