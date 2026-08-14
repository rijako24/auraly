namespace Auraly.Platform.Infrastructure.Configuration;

/// <summary>
/// Configuración del webhook y API de WhatsApp Cloud.
/// </summary>
public class WhatsAppWebhookOptions
{
    public const string SectionName = "WhatsApp:Webhook";

    /// <summary>
    /// Token para verificación del webhook en suscripción con Meta.
    /// </summary>
    public string VerifyToken { get; set; } = null!;

    /// <summary>
    /// URL base de la API de WhatsApp Cloud (incluye versión). Ej: https://graph.facebook.com/v25.0/
    /// </summary>
    public string ApiBaseUrl { get; set; } = null!;

    /// <summary>
    /// Límite operativo por mensaje. Las respuestas largas se dividen por secciones completas.
    /// </summary>
    public int MaxTextMessageLength { get; set; } = 1800;

    /// <summary>
    /// Quiet window used to coalesce consecutive customer messages into one turn.
    /// This lets short continuations share the same semantic extraction pass.
    /// </summary>
    public double InboundDebounceSeconds { get; set; } = 3d;

    public TimeSpan GetInboundDebounceDelay() =>
        TimeSpan.FromSeconds(Math.Clamp(InboundDebounceSeconds, 0.25d, 30d));


    /// <summary>Intervalo de renovacion; debe permanecer por debajo de los 25 segundos de Meta.</summary>
    public int TypingIndicatorRefreshIntervalSeconds { get; set; } = 15;
}
