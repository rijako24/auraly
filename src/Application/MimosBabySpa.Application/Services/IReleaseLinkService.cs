namespace MimosBabySpa.Application.Services;

/// <summary>
/// Genera y valida URLs firmadas para devolver conversaciones al bot.
/// El agente pulsa el link en la notificación de escalado.
/// </summary>
public interface IReleaseLinkService
{
    /// <summary>
    /// Genera URL firmada para release. Retorna null si la configuración no está lista.
    /// </summary>
    string? GenerateReleaseUrl(Guid conversationId);

    /// <summary>
    /// Valida el token HMAC para el conversationId dado.
    /// </summary>
    bool ValidateToken(Guid conversationId, string token);
}
