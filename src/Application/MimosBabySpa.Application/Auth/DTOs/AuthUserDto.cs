namespace MimosBabySpa.Application.Auth.DTOs;

public record AuthUserDto(
    Guid UserId,
    Guid TenantId,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
