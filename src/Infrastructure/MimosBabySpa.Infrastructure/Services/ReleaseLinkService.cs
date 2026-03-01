using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.Infrastructure.Services;

/// <summary>
/// Implementación de URLs firmadas para release. Usa HMAC-SHA256.
/// </summary>
public class ReleaseLinkService : IReleaseLinkService
{
    private readonly ReleaseLinkSettings _settings;
    private readonly ILogger<ReleaseLinkService> _logger;

    public ReleaseLinkService(
        IOptions<ReleaseLinkSettings> settings,
        ILogger<ReleaseLinkService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string? GenerateReleaseUrl(Guid conversationId)
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) || string.IsNullOrWhiteSpace(_settings.TokenSecret))
        {
            _logger.LogWarning("Release: BaseUrl o TokenSecret no configurados, no se genera link");
            return null;
        }

        var token = ComputeToken(conversationId);
        var urlSafeToken = token.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/release?conv={conversationId}&t={urlSafeToken}";
    }

    public bool ValidateToken(Guid conversationId, string token)
    {
        if (string.IsNullOrWhiteSpace(_settings.TokenSecret) || string.IsNullOrWhiteSpace(token))
            return false;

        var normalized = token.Replace('-', '+').Replace('_', '/');
        var pad = 4 - normalized.Length % 4;
        if (pad < 4) normalized += new string('=', pad);

        var expected = ComputeToken(conversationId);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var actualBytes = System.Text.Encoding.UTF8.GetBytes(normalized);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private string ComputeToken(Guid conversationId)
    {
        var payload = conversationId.ToString("N");
        var secretBytes = System.Text.Encoding.UTF8.GetBytes(_settings.TokenSecret);
        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }
}
