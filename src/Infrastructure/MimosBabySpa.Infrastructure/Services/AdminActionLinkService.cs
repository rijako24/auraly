using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.Infrastructure.Services;

/// <summary>
/// Genera y valida URLs firmadas para acciones administrativas (release, confirmar pago).
/// Implementa IAdminActionLinkService e IReleaseLinkService para compatibilidad.
/// </summary>
public class AdminActionLinkService : IAdminActionLinkService, IReleaseLinkService
{
    private readonly ReleaseLinkSettings _settings;
    private readonly ILogger<AdminActionLinkService> _logger;

    public AdminActionLinkService(
        IOptions<ReleaseLinkSettings> settings,
        ILogger<AdminActionLinkService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string? GenerateReleaseUrl(Guid conversationId) =>
        GenerateSignedUrl("/api/release", "conv", conversationId.ToString("N"), conversationId.ToString("N"));

    public string? GeneratePaymentConfirmationUrl(string paymentReferenceId) =>
        GenerateSignedUrl("/api/confirm-payment", "ptx", $"confirm:{paymentReferenceId}", paymentReferenceId);

    public bool ValidateReleaseToken(Guid conversationId, string token) =>
        ValidateToken(conversationId.ToString("N"), token);

    public bool ValidatePaymentConfirmationToken(string paymentReferenceId, string token) =>
        ValidateToken($"confirm:{paymentReferenceId}", token);

    public bool ValidateToken(Guid conversationId, string token) =>
        ValidateReleaseToken(conversationId, token);

    private string? GenerateSignedUrl(string path, string paramName, string payload, string paramValue)
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) || string.IsNullOrWhiteSpace(_settings.TokenSecret))
        {
            _logger.LogWarning("AdminActionLink: BaseUrl o TokenSecret no configurados");
            return null;
        }

        var token = ComputeToken(payload);
        var urlSafeToken = token.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        return $"{baseUrl}{path}?{paramName}={paramValue}&t={urlSafeToken}";
    }

    private bool ValidateToken(string payload, string token)
    {
        if (string.IsNullOrWhiteSpace(_settings.TokenSecret) || string.IsNullOrWhiteSpace(token))
            return false;

        var normalized = token.Replace('-', '+').Replace('_', '/');
        var pad = 4 - normalized.Length % 4;
        if (pad < 4) normalized += new string('=', pad);

        var expected = ComputeToken(payload);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(normalized));
    }

    private string ComputeToken(string payload)
    {
        var secretBytes = Encoding.UTF8.GetBytes(_settings.TokenSecret);
        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }
}
