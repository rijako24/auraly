namespace MimosBabySpa.Application.Identity.DTOs;

public record CreateBusinessRequest(
    string Name,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    string? Website);
