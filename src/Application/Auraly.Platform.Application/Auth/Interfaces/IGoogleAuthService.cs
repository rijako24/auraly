using Auraly.Platform.Application.Auth.DTOs;

namespace Auraly.Platform.Application.Auth.Interfaces;

public interface IGoogleAuthService
{
    Task<GoogleUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct = default);
}
