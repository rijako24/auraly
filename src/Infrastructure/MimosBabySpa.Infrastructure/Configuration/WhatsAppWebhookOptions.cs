namespace MimosBabySpa.Infrastructure.Configuration;

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
}
