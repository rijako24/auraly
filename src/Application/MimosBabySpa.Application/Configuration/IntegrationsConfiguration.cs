namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de integraciones externas por negocio (Google Calendar, futuras: Sheets, SMS, etc.).
/// </summary>
public class IntegrationsConfiguration
{
    public GoogleCalendarSettings? GoogleCalendar { get; set; }
}

/// <summary>
/// Configuración de sincronización con Google Calendar.
/// Cuando Enabled=true, las reservas se guardan en el calendario de Google.
/// </summary>
public class GoogleCalendarSettings
{
    public bool Enabled { get; set; }
    public string CalendarId { get; set; } = "primary";
}
