namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// ConfiguraciÃ³n de integraciones externas por negocio (Google Calendar, Wompi, Blob Storage, etc.).
/// Fuente Ãºnica: IIntegrationsConfigProvider lee desde IntegrationConnections.
/// </summary>
public class IntegrationsConfiguration
{
    public GoogleCalendarIntegration? GoogleCalendar { get; set; }
    public WompiIntegration? Wompi { get; set; }
}

/// <summary>
/// ConfiguraciÃ³n de sincronizaciÃ³n con Google Calendar.
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
/// ConfiguraciÃ³n de integraciÃ³n con Wompi (links de pago).
/// Si PrivateKey estÃ¡ vacÃ­o, no se generan links de pago.
/// </summary>
public class WompiIntegration
{
    public string Mode { get; set; } = "test";
    public string PrivateKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string EventsSecret { get; set; } = string.Empty;
    public string IntegritySecret { get; set; } = string.Empty;
    public string SandboxBaseUrl { get; set; } = "https://sandbox.wompi.co/v1";
    public string ProductionBaseUrl { get; set; } = "https://production.wompi.co/v1";
    public int RequestTimeoutSeconds { get; set; } = 30;
    public string CheckoutBaseUrl { get; set; } = "https://checkout.wompi.co/l/";

    /// <summary>
    /// Obtiene la URL base de la API segÃºn configuraciÃ³n.
    /// </summary>
    public string GetBaseUrl()
    {
        var isTest = !string.Equals(Mode, "production", StringComparison.OrdinalIgnoreCase);
        var url = isTest ? SandboxBaseUrl : ProductionBaseUrl;
        if (string.IsNullOrWhiteSpace(url))
            return isTest ? "https://sandbox.wompi.co/v1" : "https://production.wompi.co/v1";
        return url.TrimEnd('/');
    }
}

