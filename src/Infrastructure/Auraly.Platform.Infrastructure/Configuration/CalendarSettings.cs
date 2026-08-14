namespace Auraly.Platform.Infrastructure.Configuration;

public class CalendarSettings
{
    public const string SectionName = "Calendar";

    public string Provider { get; set; } = "Google"; // Google | Outlook
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "America/Mexico_City"; // IANA Time Zone
    public string? Scopes { get; set; } // Scopes separados por coma si es necesario
}
