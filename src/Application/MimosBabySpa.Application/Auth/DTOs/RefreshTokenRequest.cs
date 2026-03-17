namespace MimosBabySpa.Application.Auth.DTOs;

public record RefreshTokenRequest(string AccessToken, string RefreshToken);
