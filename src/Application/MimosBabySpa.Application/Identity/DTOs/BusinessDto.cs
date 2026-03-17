namespace MimosBabySpa.Application.Identity.DTOs;

public record BusinessDto(
    Guid BusinessId,
    Guid TenantId,
    string Name,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    string? Website,
    string? LogoUrl,
    bool IsActive,
    DateTime CreatedAt);
