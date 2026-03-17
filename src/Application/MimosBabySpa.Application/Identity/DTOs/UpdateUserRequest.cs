namespace MimosBabySpa.Application.Identity.DTOs;

public record UpdateUserRequest(
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? AvatarUrl);
