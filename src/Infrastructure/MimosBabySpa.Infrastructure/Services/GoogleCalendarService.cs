using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.Infrastructure.Services;

public class GoogleCalendarService : ICalendarService
{
    private const string CalendarApiBaseUrl = "https://www.googleapis.com/calendar/v3";

    private readonly HttpClient _httpClient;
    private readonly IIntegrationsConfigProvider _integrationsProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GoogleCalendarService> _logger;

    private static readonly ConcurrentDictionary<string, CachedToken> TokenCache = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class CachedToken
    {
        public required string AccessToken { get; set; }
        public DateTime Expiry { get; set; }
    }

    public GoogleCalendarService(
        HttpClient httpClient,
        IIntegrationsConfigProvider integrationsProvider,
        IUnitOfWork unitOfWork,
        ILogger<GoogleCalendarService> logger)
    {
        _httpClient = httpClient;
        _integrationsProvider = integrationsProvider;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<string> CreateEventAsync(Guid businessId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        var settings = await GetEnabledGoogleCalendarConfigAsync(businessId, cancellationToken);
        var accessToken = await EnsureAccessTokenAsync(settings, cancellationToken);
        var calendarId = await EnsureCalendarAsync(businessId, settings, accessToken, cancellationToken);
        var url = $"{CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events";

        var requestBody = new
        {
            summary = calendarEvent.Title,
            description = calendarEvent.Description,
            start = new
            {
                dateTime = calendarEvent.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                timeZone = settings.TimeZone
            },
            end = new
            {
                dateTime = calendarEvent.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                timeZone = settings.TimeZone
            },
            location = calendarEvent.Location,
            extendedProperties = calendarEvent.ExtendedProperties != null ? new { @private = calendarEvent.ExtendedProperties } : null
        };

        var responseContent = await SendJsonAsync(HttpMethod.Post, url, requestBody, accessToken, cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
        var eventId = result.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("No se pudo obtener el ID del evento creado.");

        _logger.LogInformation("Evento creado en Google Calendar con ID: {EventId}", eventId);
        return eventId;
    }

    public async Task UpdateEventAsync(Guid businessId, string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        var settings = await GetEnabledGoogleCalendarConfigAsync(businessId, cancellationToken);
        var accessToken = await EnsureAccessTokenAsync(settings, cancellationToken);
        var calendarId = await EnsureCalendarAsync(businessId, settings, accessToken, cancellationToken);
        var url = $"{CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

        var requestBody = new
        {
            summary = calendarEvent.Title,
            description = calendarEvent.Description,
            start = new { dateTime = calendarEvent.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = settings.TimeZone },
            end = new { dateTime = calendarEvent.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = settings.TimeZone },
            location = calendarEvent.Location,
            extendedProperties = calendarEvent.ExtendedProperties != null ? new { @private = calendarEvent.ExtendedProperties } : null
        };

        await SendJsonAsync(HttpMethod.Put, url, requestBody, accessToken, cancellationToken);
        _logger.LogInformation("Evento actualizado en Google Calendar con ID: {EventId}", eventId);
    }

    public async Task DeleteEventAsync(Guid businessId, string eventId, CancellationToken cancellationToken = default)
    {
        var settings = await GetEnabledGoogleCalendarConfigAsync(businessId, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.CalendarId))
            return;

        var accessToken = await EnsureAccessTokenAsync(settings, cancellationToken);
        var url = $"{CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(settings.CalendarId)}/events/{Uri.EscapeDataString(eventId)}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Error al eliminar evento en Google Calendar. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            throw new InvalidOperationException(
                $"Error al eliminar evento en Google Calendar: {response.StatusCode} - {responseContent}");
        }

        _logger.LogInformation("Evento eliminado en Google Calendar con ID: {EventId}", eventId);
    }

    private async Task<GoogleCalendarIntegration> GetEnabledGoogleCalendarConfigAsync(Guid businessId, CancellationToken ct)
    {
        var integrations = await _integrationsProvider.GetAsync(businessId, ct);
        var settings = integrations?.GoogleCalendar;
        if (settings is null || !settings.Enabled)
            throw new InvalidOperationException($"Google Calendar no configurado para BusinessId={businessId}");

        ValidateCredentials(settings);
        return settings;
    }

    private async Task<string> EnsureCalendarAsync(
        Guid businessId,
        GoogleCalendarIntegration settings,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.CalendarId))
            return settings.CalendarId;

        if (!settings.AutoCreateCalendar)
            throw new InvalidOperationException($"Google Calendar no tiene calendarId para BusinessId={businessId}");

        var connection = await _unitOfWork.IntegrationConnections.GetByBusinessProviderCapabilityAsync(
            businessId,
            IntegrationProvider.GoogleCalendar,
            IntegrationCapability.Calendar,
            cancellationToken)
            ?? throw new InvalidOperationException($"No existe IntegrationConnection de Google Calendar para BusinessId={businessId}");

        var currentSettings = ParseSettings(connection.SettingsJson);
        var currentCalendarId = GetString(currentSettings, "calendarId");
        if (!string.IsNullOrWhiteSpace(currentCalendarId))
            return currentCalendarId;

        var calendarId = await CreateSecondaryCalendarAsync(settings, accessToken, cancellationToken);
        var shared = await ShareCalendarAsync(calendarId, settings, accessToken, cancellationToken);
        var inserted = await TryInsertCalendarInAuthenticatedCalendarListAsync(calendarId, settings, accessToken, cancellationToken);

        currentSettings["calendarId"] = calendarId;
        currentSettings["calendarCreatedAtUtc"] = DateTime.UtcNow;
        currentSettings["calendarCreatedBy"] = settings.OwnerEmail;
        if (shared)
            currentSettings["calendarSharedAtUtc"] = DateTime.UtcNow;
        if (!inserted && settings.InsertIntoSharedCalendarList && !string.IsNullOrWhiteSpace(settings.SharedWithEmail))
            currentSettings["calendarListInsertStatus"] = "requires_customer_oauth_or_domain_delegation";

        connection.AccountIdentifier = calendarId;
        connection.SettingsJson = currentSettings.ToJsonString(JsonOptions);
        connection.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        settings.CalendarId = calendarId;
        return calendarId;
    }

    private async Task<string> CreateSecondaryCalendarAsync(
        GoogleCalendarIntegration settings,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var summary = string.IsNullOrWhiteSpace(settings.CalendarSummary)
            ? "Auraly - Reservas"
            : settings.CalendarSummary.Trim();

        var requestBody = new
        {
            summary,
            description = $"Calendario de reservas administrado por Auraly ({settings.OwnerEmail}).",
            timeZone = settings.TimeZone
        };

        var responseContent = await SendJsonAsync(HttpMethod.Post, $"{CalendarApiBaseUrl}/calendars", requestBody, accessToken, cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
        var calendarId = result.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("No se pudo obtener el ID del calendario creado.");

        _logger.LogInformation(
            "Calendario Google creado. CalendarId={CalendarId}, Summary={Summary}, Owner={OwnerEmail}",
            calendarId,
            summary,
            settings.OwnerEmail);

        return calendarId;
    }

    private async Task<bool> ShareCalendarAsync(
        string calendarId,
        GoogleCalendarIntegration settings,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SharedWithEmail))
            return false;

        if (string.Equals(settings.SharedWithEmail.Trim(), settings.OwnerEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var role = NormalizeAclRole(settings.SharedRole);
        var requestBody = new
        {
            role,
            scope = new
            {
                type = "user",
                value = settings.SharedWithEmail.Trim()
            }
        };
        var url = $"{CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/acl?sendNotifications={settings.SendSharingNotifications.ToString().ToLowerInvariant()}";

        await SendJsonAsync(HttpMethod.Post, url, requestBody, accessToken, cancellationToken, HttpStatusCode.Conflict);
        _logger.LogInformation(
            "Calendario Google compartido. CalendarId={CalendarId}, SharedWith={SharedWithEmail}, Role={Role}",
            calendarId,
            settings.SharedWithEmail,
            role);
        return true;
    }

    private async Task<bool> TryInsertCalendarInAuthenticatedCalendarListAsync(
        string calendarId,
        GoogleCalendarIntegration settings,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!settings.InsertIntoSharedCalendarList)
            return false;

        if (!string.IsNullOrWhiteSpace(settings.SharedWithEmail)
            && !string.Equals(settings.SharedWithEmail, settings.OwnerEmail, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "CalendarList.insert para {SharedWithEmail} requiere OAuth del cliente o domain-wide delegation; con el token global solo se puede operar como {OwnerEmail}.",
                settings.SharedWithEmail,
                settings.OwnerEmail);
            return false;
        }

        var requestBody = new { id = calendarId, selected = true };
        await SendJsonAsync(HttpMethod.Post, $"{CalendarApiBaseUrl}/users/me/calendarList", requestBody, accessToken, cancellationToken, HttpStatusCode.Conflict);
        return true;
    }

    private async Task<string> EnsureAccessTokenAsync(
        GoogleCalendarIntegration settings,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetTokenCacheKey(settings);
        if (TokenCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow.AddMinutes(5))
            return cached.AccessToken;

        var tokenUrl = "https://oauth2.googleapis.com/token";
        var requestBody = new Dictionary<string, string>
        {
            { "client_id", settings.ClientId },
            { "client_secret", settings.ClientSecret },
            { "refresh_token", settings.RefreshToken },
            { "grant_type", "refresh_token" }
        };

        using var content = new FormUrlEncodedContent(requestBody);
        var response = await _httpClient.PostAsync(tokenUrl, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Error al obtener access token de Google. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            throw new InvalidOperationException(
                $"Error al obtener access token de Google: {response.StatusCode} - {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
        var accessToken = tokenResponse.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token no presente en respuesta de Google");
        var expiresIn = tokenResponse.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        var expiry = DateTime.UtcNow.AddSeconds(expiresIn - 300);

        TokenCache[cacheKey] = new CachedToken { AccessToken = accessToken, Expiry = expiry };
        _logger.LogDebug("Access token de Google obtenido para OwnerEmail={OwnerEmail}. Expira en {ExpiresIn}s", settings.OwnerEmail, expiresIn);
        return accessToken;
    }

    private async Task<string> SendJsonAsync(
        HttpMethod method,
        string url,
        object body,
        string accessToken,
        CancellationToken cancellationToken,
        params HttpStatusCode[] acceptedStatusCodes)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode && !acceptedStatusCodes.Contains(response.StatusCode))
        {
            _logger.LogError(
                "Error en Google Calendar API. Method={Method}, Url={Url}, Status={StatusCode}, Response={Response}",
                method,
                url,
                response.StatusCode,
                responseContent);
            throw new InvalidOperationException(
                $"Error en Google Calendar API: {response.StatusCode} - {responseContent}");
        }

        return responseContent;
    }

    private static void ValidateCredentials(GoogleCalendarIntegration settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.ClientSecret)
            || string.IsNullOrWhiteSpace(settings.RefreshToken)
            || string.IsNullOrWhiteSpace(settings.OwnerEmail))
        {
            throw new InvalidOperationException(
                $"Credenciales globales de Google Calendar incompletas. Revisa SystemConfigurationId={settings.PlatformConfigurationId}.");
        }
    }

    private static JsonObject ParseSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return [];

        try
        {
            return JsonNode.Parse(json)?.AsObject() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? GetString(JsonObject values, string key)
    {
        if (!values.TryGetPropertyValue(key, out var value) || value is null)
            return null;

        return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
    }

    private static string NormalizeAclRole(string? role)
    {
        return role?.Trim() switch
        {
            "freeBusyReader" => "freeBusyReader",
            "reader" => "reader",
            "writer" => "writer",
            "owner" => "owner",
            _ => "writer"
        };
    }

    private static string GetTokenCacheKey(GoogleCalendarIntegration settings)
    {
        var owner = string.IsNullOrWhiteSpace(settings.OwnerEmail) ? "default" : settings.OwnerEmail.Trim().ToLowerInvariant();
        return $"calendar_token:{settings.PlatformConfigurationId}:{owner}";
    }
}
