namespace Auraly.Platform.Infrastructure.Configuration;

/// <summary>
/// Configuración para el link de release (devolver conversación al bot).
/// El agente recibe un link firmado en la notificación de escalado.
/// </summary>
public class ReleaseLinkSettings
{
    public const string SectionName = "Release";

    /// <summary>
    /// URL base del API (ej: https://api.mimos.com o la Function App URL).
    /// Sin barra final. El path /api/release se concatena.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Secreto para firmar el token HMAC. Mínimo 16 caracteres.
    /// </summary>
    public string TokenSecret { get; set; } = string.Empty;
}
