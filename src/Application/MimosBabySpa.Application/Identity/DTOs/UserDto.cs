namespace MimosBabySpa.Application.Identity.DTOs;

public record UserDto(
    Guid UserId,
    Guid TenantId,
    Guid? PartyId,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? AvatarUrl,
    bool IsActive,
    bool EmailConfirmed,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    IReadOnlyList<UserRoleDto> Roles);
