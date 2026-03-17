using MimosBabySpa.Application.Auth.DTOs;

namespace MimosBabySpa.Application.Auth.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress = null, string? deviceInfo = null, CancellationToken ct = default);
    Task<LoginResponse> GoogleLoginAsync(GoogleLoginRequest request, string? ipAddress = null, string? deviceInfo = null, CancellationToken ct = default);
    Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress = null, CancellationToken ct = default);
    Task RevokeTokenAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeAllUserTokensAsync(Guid userId, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<AuthUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
