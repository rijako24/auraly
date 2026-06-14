namespace MimosBabySpa.Application.Identity.DTOs;

public record IntegrationSettingsDto(
    GoogleCalendarIntegrationDto GoogleCalendar,
    WompiIntegrationDto Wompi);

public record GoogleCalendarIntegrationDto(
    bool IsEnabled,
    string CalendarId,
    string TimeZone,
    string? Scopes,
    bool HasClientId,
    bool HasClientSecret,
    bool HasRefreshToken,
    string? LastError,
    DateTime? LastSyncAt);

public record WompiIntegrationDto(
    bool IsEnabled,
    bool UseSandbox,
    string SandboxBaseUrl,
    string ProductionBaseUrl,
    int RequestTimeoutSeconds,
    string CheckoutBaseUrl,
    bool HasPrivateKey,
    bool HasPublicKey,
    bool HasEventsSecret,
    bool HasIntegritySecret,
    string? LastError,
    DateTime? LastSyncAt);

public record UpdateGoogleCalendarIntegrationRequest(
    bool IsEnabled,
    string CalendarId,
    string TimeZone,
    string? Scopes,
    string? ClientId,
    string? ClientSecret,
    string? RefreshToken);

public record UpdateWompiIntegrationRequest(
    bool IsEnabled,
    bool UseSandbox,
    string SandboxBaseUrl,
    string ProductionBaseUrl,
    int RequestTimeoutSeconds,
    string CheckoutBaseUrl,
    string? PrivateKey,
    string? PublicKey,
    string? EventsSecret,
    string? IntegritySecret);
