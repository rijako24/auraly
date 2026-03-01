namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de integraciones externas por negocio (Google Calendar, Wompi, futuras: Sheets, SMS, etc.).
/// Fuente única: IIntegrationsConfigProvider lee desde BusinessConfiguration (Key=Integrations).
/// </summary>
public class IntegrationsConfiguration
{
    public GoogleCalendarIntegration? GoogleCalendar { get; set; }
    public WompiIntegration? Wompi { get; set; }
}

/// <summary>
/// Configuración de sincronización con Google Calendar.
/// Cuando Enabled=true, las reservas se guardan en el calendario de Google.
/// </summary>
public class GoogleCalendarIntegration
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Google";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string CalendarId { get; set; } = "primary";
    public string TimeZone { get; set; } = "America/Bogota";
    public string? Scopes { get; set; }
}

/// <summary>
/// Configuración de integración con Wompi (links de pago).
/// Si PrivateKey está vacío, no se generan links de pago.
/// </summary>
public class WompiIntegration
{
    public string PrivateKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string EventsSecret { get; set; } = string.Empty;
    public string IntegritySecret { get; set; } = string.Empty;
    public bool UseSandbox { get; set; } = true;
    public string SandboxBaseUrl { get; set; } = "https://sandbox.wompi.co/v1";
    public string ProductionBaseUrl { get; set; } = "https://production.wompi.co/v1";
    public int RequestTimeoutSeconds { get; set; } = 30;
    public string CheckoutBaseUrl { get; set; } = "https://checkout.wompi.co/l/";

    /// <summary>
    /// Obtiene la URL base de la API según configuración.
    /// </summary>
    public string GetBaseUrl()
    {
        var url = UseSandbox ? SandboxBaseUrl : ProductionBaseUrl;
        if (string.IsNullOrWhiteSpace(url))
            return UseSandbox ? "https://sandbox.wompi.co/v1" : "https://production.wompi.co/v1";
        return url.TrimEnd('/');
    }
}
