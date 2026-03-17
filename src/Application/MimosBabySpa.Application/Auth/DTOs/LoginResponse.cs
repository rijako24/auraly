namespace MimosBabySpa.Application.Auth.DTOs;

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    AuthUserDto User,
    string CorrelationId);
