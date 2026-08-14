namespace Auraly.Platform.Application.Auth.DTOs;

public record GoogleUserInfo(
    string GoogleId,
    string Email,
    string FirstName,
    string LastName,
    string? PictureUrl,
    bool EmailVerified);
