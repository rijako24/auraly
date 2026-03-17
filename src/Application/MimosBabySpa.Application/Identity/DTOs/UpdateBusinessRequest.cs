namespace MimosBabySpa.Application.Identity.DTOs;

public record UpdateBusinessRequest(
    string? Name,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    string? Website,
    string? LogoUrl);
