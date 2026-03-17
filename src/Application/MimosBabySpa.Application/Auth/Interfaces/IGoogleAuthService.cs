using MimosBabySpa.Application.Auth.DTOs;

namespace MimosBabySpa.Application.Auth.Interfaces;

public interface IGoogleAuthService
{
    Task<GoogleUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct = default);
}
