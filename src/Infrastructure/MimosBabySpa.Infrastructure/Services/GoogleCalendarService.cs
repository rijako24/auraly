using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.Infrastructure.Services;

public class GoogleCalendarService : ICalendarService
{
    private readonly HttpClient _httpClient;
    private readonly IIntegrationsConfigProvider _integrationsProvider;
    private readonly ILogger<GoogleCalendarService> _logger;

    private static readonly ConcurrentDictionary<string, CachedToken> TokenCache = new();

    private sealed class CachedToken
    {
        public required string AccessToken { get; set; }
        public DateTime Expiry { get; set; }
    }

    public GoogleCalendarService(
        HttpClient httpClient,
        IIntegrationsConfigProvider integrationsProvider,
        ILogger<GoogleCalendarService> logger)
    {
        _httpClient = httpClient;
        _integrationsProvider = integrationsProvider;
        _logger = logger;
    }

    public async Task<string> CreateEventAsync(Guid businessId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        var gc = await GetGoogleCalendarConfigAsync(businessId, cancellationToken);
        if (gc == null)
            throw new InvalidOperationException($"Google Calendar no configurado para BusinessId={businessId}");

        var settings = gc;
        await EnsureAccessTokenAsync(businessId, settings, cancellationToken);

        var calendarId = string.IsNullOrEmpty(settings.CalendarId) ? "primary" : settings.CalendarId;
        var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events";

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

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var accessToken = GetCachedToken(businessId);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error al crear evento en Google Calendar. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode, responseContent);
                throw new InvalidOperationException(
                    $"Error al crear evento en Google Calendar: {response.StatusCode} - {responseContent}");
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var eventId = result.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("No se pudo obtener el ID del evento creado.");

            _logger.LogInformation("Evento creado en Google Calendar con ID: {EventId}", eventId);
            return eventId;
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task UpdateEventAsync(Guid businessId, string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        var gc = await GetGoogleCalendarConfigAsync(businessId, cancellationToken);
        if (gc == null)
            throw new InvalidOperationException($"Google Calendar no configurado para BusinessId={businessId}");

        await EnsureAccessTokenAsync(businessId, gc, cancellationToken);

        var calendarId = string.IsNullOrEmpty(gc.CalendarId) ? "primary" : gc.CalendarId;
        var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

        var requestBody = new
        {
            summary = calendarEvent.Title,
            description = calendarEvent.Description,
            start = new { dateTime = calendarEvent.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = gc.TimeZone },
            end = new { dateTime = calendarEvent.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = gc.TimeZone },
            location = calendarEvent.Location,
            extendedProperties = calendarEvent.ExtendedProperties != null ? new { @private = calendarEvent.ExtendedProperties } : null
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var accessToken = GetCachedToken(businessId);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var response = await _httpClient.PutAsync(url, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error al actualizar evento en Google Calendar. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode, responseContent);
                throw new InvalidOperationException(
                    $"Error al actualizar evento en Google Calendar: {response.StatusCode} - {responseContent}");
            }

            _logger.LogInformation("Evento actualizado en Google Calendar con ID: {EventId}", eventId);
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task DeleteEventAsync(Guid businessId, string eventId, CancellationToken cancellationToken = default)
    {
        var gc = await GetGoogleCalendarConfigAsync(businessId, cancellationToken);
        if (gc == null)
            throw new InvalidOperationException($"Google Calendar no configurado para BusinessId={businessId}");

        await EnsureAccessTokenAsync(businessId, gc, cancellationToken);

        var calendarId = string.IsNullOrEmpty(gc.CalendarId) ? "primary" : gc.CalendarId;
        var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

        var accessToken = GetCachedToken(businessId);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var response = await _httpClient.DeleteAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
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
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<bool> IsAvailableAsync(Guid businessId, DateTime startDateTime, DateTime endDateTime, CancellationToken cancellationToken = default)
    {
        var gc = await GetGoogleCalendarConfigAsync(businessId, cancellationToken);
        if (gc == null)
            return true;

        try
        {
            await EnsureAccessTokenAsync(businessId, gc, cancellationToken);

            var calendarId = string.IsNullOrEmpty(gc.CalendarId) ? "primary" : gc.CalendarId;
            var timeMin = startDateTime.ToString("yyyy-MM-ddTHH:mm:ss");
            var timeMax = endDateTime.ToString("yyyy-MM-ddTHH:mm:ss");
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events" +
                     $"?timeMin={Uri.EscapeDataString(timeMin)}&timeMax={Uri.EscapeDataString(timeMax)}" +
                     $"&singleEvents=true&orderBy=startTime";

            var accessToken = GetCachedToken(businessId);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Error al consultar disponibilidad en Google Calendar. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, responseContent);
                    return true;
                }

                var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (result.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end))
                            continue;

                        if (!TryParseEventTime(start, out var eventStart) || !TryParseEventTime(end, out var eventEnd))
                            continue;

                        if (eventStart < endDateTime && eventEnd > startDateTime)
                        {
                            _logger.LogDebug(
                                "Horario no disponible: conflicto con evento existente del {EventStart} al {EventEnd}",
                                eventStart, eventEnd);
                            return false;
                        }
                    }
                }

                return true;
            }
            finally
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar disponibilidad en Google Calendar");
            return true;
        }
    }

    private static bool TryParseEventTime(JsonElement el, out DateTime dt)
    {
        dt = default;
        if (el.TryGetProperty("dateTime", out var dtProp))
        {
            var s = dtProp.GetString();
            return !string.IsNullOrEmpty(s) && DateTime.TryParse(s, out dt);
        }
        if (el.TryGetProperty("date", out var dProp))
        {
            var s = dProp.GetString();
            return !string.IsNullOrEmpty(s) && DateTime.TryParse(s, out dt);
        }
        return false;
    }

    private async Task<GoogleCalendarIntegration?> GetGoogleCalendarConfigAsync(Guid businessId, CancellationToken ct)
    {
        var integrations = await _integrationsProvider.GetAsync(businessId, ct);
        return integrations?.GoogleCalendar;
    }

    private string GetCachedToken(Guid businessId)
    {
        var key = $"calendar_token:{businessId:N}";
        if (TokenCache.TryGetValue(key, out var cached) && cached.Expiry > DateTime.UtcNow.AddMinutes(5))
            return cached.AccessToken;

        throw new InvalidOperationException(
            "Token de Google Calendar no disponible. Debe llamarse EnsureAccessTokenAsync antes.");
    }

    private void SetCachedToken(Guid businessId, string accessToken, DateTime expiry)
    {
        var key = $"calendar_token:{businessId:N}";
        TokenCache[key] = new CachedToken { AccessToken = accessToken, Expiry = expiry };
    }

    private async Task EnsureAccessTokenAsync(
        Guid businessId,
        GoogleCalendarIntegration settings,
        CancellationToken cancellationToken)
    {
        var key = $"calendar_token:{businessId:N}";
        if (TokenCache.TryGetValue(key, out var cached) && cached.Expiry > DateTime.UtcNow.AddMinutes(5))
            return;

        var tokenUrl = "https://oauth2.googleapis.com/token";
        var requestBody = new Dictionary<string, string>
        {
            { "client_id", settings.ClientId },
            { "client_secret", settings.ClientSecret },
            { "refresh_token", settings.RefreshToken },
            { "grant_type", "refresh_token" }
        };

        var content = new FormUrlEncodedContent(requestBody);
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

        SetCachedToken(businessId, accessToken, expiry);
        _logger.LogDebug("Access token de Google obtenido para BusinessId={BusinessId}. Expira en {ExpiresIn}s", businessId, expiresIn);
    }
}
