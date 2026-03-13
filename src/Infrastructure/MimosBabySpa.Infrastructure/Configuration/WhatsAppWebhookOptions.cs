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
}
