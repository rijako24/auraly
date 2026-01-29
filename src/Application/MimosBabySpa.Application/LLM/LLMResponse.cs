namespace MimosBabySpa.Application.LLM;

/// <summary>
/// Respuesta del LLM
/// </summary>
public class LLMResponse
{
    /// <summary>
    /// Contenido de la respuesta
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Indica si la respuesta fue exitosa
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Mensaje de error si falló
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Razón de finalización (stop, length, content_filter, etc.)
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// Uso de tokens
    /// </summary>
    public TokenUsage? Usage { get; set; }
}
