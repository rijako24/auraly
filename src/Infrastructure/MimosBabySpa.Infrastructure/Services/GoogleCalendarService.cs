using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.Infrastructure.Services;

public class GoogleCalendarService : ICalendarService
{
    private readonly HttpClient _httpClient;
    private readonly CalendarSettings _settings;
    private readonly ILogger<GoogleCalendarService> _logger;
    private string? _accessToken;
    private DateTime? _tokenExpiry;

    public GoogleCalendarService(
        HttpClient httpClient,
        IOptions<CalendarSettings> settings,
        ILogger<GoogleCalendarService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> CreateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAccessTokenAsync(cancellationToken);

            var calendarId = string.IsNullOrEmpty(_settings.CalendarId) ? "primary" : _settings.CalendarId;
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events";

            var requestBody = new
            {
                summary = calendarEvent.Title,
                description = calendarEvent.Description,
                start = new
                {
                    dateTime = calendarEvent.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeZone = _settings.TimeZone
                },
                end = new
                {
                    dateTime = calendarEvent.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeZone = _settings.TimeZone
                },
                location = calendarEvent.Location,
                extendedProperties = calendarEvent.ExtendedProperties != null ? new
                {
                    @private = calendarEvent.ExtendedProperties
                } : null
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error al crear evento en Google Calendar. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    responseContent);
                throw new InvalidOperationException(
                    $"Error al crear evento en Google Calendar: {response.StatusCode} - {responseContent}");
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var eventId = result.GetProperty("id").GetString();

            if (string.IsNullOrEmpty(eventId))
            {
                throw new InvalidOperationException("No se pudo obtener el ID del evento creado.");
            }

            _logger.LogInformation("Evento creado en Google Calendar con ID: {EventId}", eventId);
            return eventId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear evento en Google Calendar");
            throw;
        }
    }

    public async Task UpdateEventAsync(string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAccessTokenAsync(cancellationToken);

            var calendarId = string.IsNullOrEmpty(_settings.CalendarId) ? "primary" : _settings.CalendarId;
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

            var requestBody = new
            {
                summary = calendarEvent.Title,
                description = calendarEvent.Description,
                start = new
                {
                    dateTime = calendarEvent.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeZone = _settings.TimeZone
                },
                end = new
                {
                    dateTime = calendarEvent.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    timeZone = _settings.TimeZone
                },
                location = calendarEvent.Location,
                extendedProperties = calendarEvent.ExtendedProperties != null ? new
                {
                    @private = calendarEvent.ExtendedProperties
                } : null
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.PutAsync(url, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error al actualizar evento en Google Calendar. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    responseContent);
                throw new InvalidOperationException(
                    $"Error al actualizar evento en Google Calendar: {response.StatusCode} - {responseContent}");
            }

            _logger.LogInformation("Evento actualizado en Google Calendar con ID: {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar evento en Google Calendar");
            throw;
        }
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAccessTokenAsync(cancellationToken);

            var calendarId = string.IsNullOrEmpty(_settings.CalendarId) ? "primary" : _settings.CalendarId;
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.DeleteAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Error al eliminar evento en Google Calendar. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    responseContent);
                throw new InvalidOperationException(
                    $"Error al eliminar evento en Google Calendar: {response.StatusCode} - {responseContent}");
            }

            _logger.LogInformation("Evento eliminado en Google Calendar con ID: {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar evento en Google Calendar");
            throw;
        }
    }

    public async Task<bool> IsAvailableAsync(DateTime startDateTime, DateTime endDateTime, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAccessTokenAsync(cancellationToken);

            var calendarId = string.IsNullOrEmpty(_settings.CalendarId) ? "primary" : _settings.CalendarId;
            
            // Formatear fechas en formato RFC3339 para la API de Google Calendar
            var timeMin = startDateTime.ToString("yyyy-MM-ddTHH:mm:ss");
            var timeMax = endDateTime.ToString("yyyy-MM-ddTHH:mm:ss");
            
            // Consultar eventos en el rango de tiempo especificado
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events" +
                     $"?timeMin={Uri.EscapeDataString(timeMin)}" +
                     $"&timeMax={Uri.EscapeDataString(timeMax)}" +
                     $"&singleEvents=true" +
                     $"&orderBy=startTime";

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.GetAsync(url, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error al consultar disponibilidad en Google Calendar. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    responseContent);
                // Si hay error al consultar, asumimos que está disponible para no bloquear reservas
                return true;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            // Verificar si hay eventos en el rango de tiempo
            if (result.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    // Verificar si el evento tiene fecha/hora de inicio y fin
                    if (item.TryGetProperty("start", out var start) && 
                        item.TryGetProperty("end", out var end))
                    {
                        DateTime eventStart, eventEnd;

                        // Manejar tanto dateTime como date (eventos de todo el día)
                        if (start.TryGetProperty("dateTime", out var startDateTimeProp))
                        {
                            var startDateTimeStr = startDateTimeProp.GetString();
                            if (string.IsNullOrEmpty(startDateTimeStr) || !DateTime.TryParse(startDateTimeStr, out eventStart))
                                continue;
                        }
                        else if (start.TryGetProperty("date", out var startDateProp))
                        {
                            var startDateStr = startDateProp.GetString();
                            if (string.IsNullOrEmpty(startDateStr) || !DateTime.TryParse(startDateStr, out eventStart))
                                continue;
                        }
                        else
                        {
                            continue;
                        }

                        if (end.TryGetProperty("dateTime", out var endDateTimeProp))
                        {
                            var endDateTimeStr = endDateTimeProp.GetString();
                            if (string.IsNullOrEmpty(endDateTimeStr) || !DateTime.TryParse(endDateTimeStr, out eventEnd))
                                continue;
                        }
                        else if (end.TryGetProperty("date", out var endDateProp))
                        {
                            var endDateStr = endDateProp.GetString();
                            if (string.IsNullOrEmpty(endDateStr) || !DateTime.TryParse(endDateStr, out eventEnd))
                                continue;
                        }
                        else
                        {
                            continue;
                        }

                        // Verificar si hay solapamiento
                        // Un evento se solapa si: eventStart < endDateTime && eventEnd > startDateTime
                        if (eventStart < endDateTime && eventEnd > startDateTime)
                        {
                            _logger.LogDebug(
                                "Horario no disponible: conflicto con evento existente del {EventStart} al {EventEnd}",
                                eventStart,
                                eventEnd);
                            return false;
                        }
                    }
                }
            }

            // No hay conflictos, el horario está disponible
            _logger.LogDebug(
                "Horario disponible del {StartDateTime} al {EndDateTime}",
                startDateTime,
                endDateTime);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar disponibilidad en Google Calendar");
            // Si hay error, asumimos que está disponible para no bloquear reservas
            return true;
        }
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        // Si el token aún es válido, no hacer nada
        if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiry.HasValue && _tokenExpiry.Value > DateTime.UtcNow.AddMinutes(5))
        {
            return;
        }

        try
        {
            // Obtener nuevo access token usando refresh token
            var tokenUrl = "https://oauth2.googleapis.com/token";
            var requestBody = new Dictionary<string, string>
            {
                { "client_id", _settings.ClientId },
                { "client_secret", _settings.ClientSecret },
                { "refresh_token", _settings.RefreshToken },
                { "grant_type", "refresh_token" }
            };

            var content = new FormUrlEncodedContent(requestBody);
            var response = await _httpClient.PostAsync(tokenUrl, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error al obtener access token de Google. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    responseContent);
                throw new InvalidOperationException(
                    $"Error al obtener access token de Google: {response.StatusCode} - {responseContent}");
            }

            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
            _accessToken = tokenResponse.GetProperty("access_token").GetString();
            
            var expiresIn = tokenResponse.TryGetProperty("expires_in", out var expiresInProp) 
                ? expiresInProp.GetInt32() 
                : 3600; // Default 1 hour
            
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 300); // Refresh 5 minutes before expiry

            _logger.LogDebug("Access token de Google obtenido exitosamente. Expira en {ExpiresIn} segundos", expiresIn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener access token de Google");
            throw;
        }
    }
}
