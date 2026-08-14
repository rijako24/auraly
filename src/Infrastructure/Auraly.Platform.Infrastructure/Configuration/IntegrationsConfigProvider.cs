using System.Text.Json;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Infrastructure.Configuration;

public class IntegrationsConfigProvider : IIntegrationsConfigProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IntegrationsConfigProvider> _logger;

    public IntegrationsConfigProvider(
        IUnitOfWork unitOfWork,
        ILogger<IntegrationsConfigProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IntegrationsConfiguration?> GetAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        var connections = await _unitOfWork.IntegrationConnections.GetByBusinessIdAsync(businessId, cancellationToken);
        if (connections.Count == 0)
            return null;

        var googleConnection = connections.FirstOrDefault(c =>
            c.ConnectionType == ConnectionType.Integration &&
            c.Provider == (int)IntegrationProvider.GoogleCalendar &&
            c.Capability == (int)IntegrationCapability.Calendar);

        return new IntegrationsConfiguration
        {
            GoogleCalendar = await BuildGoogleCalendarAsync(googleConnection, cancellationToken),
            Wompi = BuildWompi(connections.FirstOrDefault(c =>
                c.ConnectionType == ConnectionType.Integration &&
                c.Provider == (int)IntegrationProvider.Wompi &&
                c.Capability == (int)IntegrationCapability.Payments))
        };
    }

    private async Task<GoogleCalendarIntegration?> BuildGoogleCalendarAsync(
        IntegrationConnection? connection,
        CancellationToken cancellationToken)
    {
        if (connection is null)
            return null;

        var settings = ParseJson(connection.SettingsJson);
        var platformConfigurationId = GetInt(
            settings,
            "platformConfigurationId",
            (int)SystemConfigurationKey.GoogleCalendarPlatformCredentials);
        var platformSecrets = await GetPlatformGoogleCalendarConfigurationAsync(platformConfigurationId, cancellationToken);

        var calendarSummary = GetString(settings, "calendarSummary");
        if (string.IsNullOrWhiteSpace(calendarSummary))
            calendarSummary = connection.Name;

        return new GoogleCalendarIntegration
        {
            Enabled = connection.IsEnabled,
            Provider = "Google",
            PlatformConfigurationId = platformConfigurationId,
            OwnerEmail = GetString(platformSecrets, "ownerEmail"),
            ClientId = GetString(platformSecrets, "clientId"),
            ClientSecret = GetString(platformSecrets, "clientSecret"),
            RefreshToken = GetString(platformSecrets, "refreshToken"),
            CalendarId = GetString(settings, "calendarId"),
            CalendarSummary = calendarSummary,
            TimeZone = GetString(settings, "timeZone", "America/Bogota"),
            Scopes = GetNullableString(platformSecrets, "scopes"),
            AutoCreateCalendar = GetBool(settings, "autoCreateCalendar", true),
            SharedWithEmail = GetNullableString(settings, "sharedWithEmail"),
            SharedRole = GetString(settings, "sharedRole", "writer"),
            SendSharingNotifications = GetBool(settings, "sendSharingNotifications", true),
            InsertIntoSharedCalendarList = GetBool(settings, "insertIntoSharedCalendarList", false)
        };
    }

    private async Task<Dictionary<string, JsonElement>> GetPlatformGoogleCalendarConfigurationAsync(
        int platformConfigurationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = await _unitOfWork.SystemConfigurations.GetByKeyAsync((SystemConfigurationKey)platformConfigurationId);
            return ParseJson(config?.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo leer la configuracion global de Google Calendar con SystemConfigurationId={SystemConfigurationId}",
                platformConfigurationId);
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private WompiIntegration? BuildWompi(IntegrationConnection? connection)
    {
        if (connection is null)
            return null;

        var settings = ParseJson(connection.SettingsJson);
        var secrets = ParseJson(connection.SecretsJson);
        var mode = NormalizeWompiMode(GetString(settings, "mode", "test"));
        var modeSecrets = GetObject(secrets, mode);

        return new WompiIntegration
        {
            Mode = mode,
            PrivateKey = GetString(modeSecrets, "privateKey", GetString(secrets, "privateKey")),
            PublicKey = GetString(modeSecrets, "publicKey", GetString(secrets, "publicKey")),
            EventsSecret = GetString(modeSecrets, "eventsSecret", GetString(secrets, "eventsSecret")),
            IntegritySecret = GetString(modeSecrets, "integritySecret", GetString(secrets, "integritySecret")),
            SandboxBaseUrl = GetString(settings, "sandboxBaseUrl", "https://sandbox.wompi.co/v1"),
            ProductionBaseUrl = GetString(settings, "productionBaseUrl", "https://production.wompi.co/v1"),
            RequestTimeoutSeconds = GetInt(settings, "requestTimeoutSeconds", 30),
            CheckoutBaseUrl = GetString(settings, "checkoutBaseUrl", "https://checkout.wompi.co/l/")
        };
    }

    private Dictionary<string, JsonElement> ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Integration connection JSON invalido");
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetString(Dictionary<string, JsonElement> values, string key, string fallback = "")
    {
        var value = GetNullableString(values, key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string? GetNullableString(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool GetBool(Dictionary<string, JsonElement> values, string key, bool fallback)
    {
        if (!values.TryGetValue(key, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static int GetInt(Dictionary<string, JsonElement> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static Dictionary<string, JsonElement> GetObject(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        return value.EnumerateObject()
            .ToDictionary(
                p => p.Name,
                p => p.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeWompiMode(string? mode)
    {
        return string.Equals(mode, "production", StringComparison.OrdinalIgnoreCase)
            ? "production"
            : "test";
    }
}
