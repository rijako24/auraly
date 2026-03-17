using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Auth.DTOs;
using MimosBabySpa.Application.Auth.Interfaces;

namespace MimosBabySpa.Infrastructure.Identity;

public class GoogleAuthService : IGoogleAuthService
{
    private readonly GoogleAuthSettings _settings;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        IOptions<GoogleAuthSettings> settings,
        ILogger<GoogleAuthService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<GoogleUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct)
    {
        var validationSettings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { _settings.ClientId }
        };

        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);

        return new GoogleUserInfo(
            GoogleId: payload.Subject,
            Email: payload.Email ?? string.Empty,
            FirstName: payload.GivenName ?? string.Empty,
            LastName: payload.FamilyName ?? string.Empty,
            PictureUrl: payload.Picture,
            EmailVerified: payload.EmailVerified);
    }
}
