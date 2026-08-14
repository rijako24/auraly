namespace Auraly.Platform.Application.Identity.DTOs;

public record CreateUserRequest(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    Guid? PartyId = null);
