namespace MimosBabySpa.Application.LLM;

/// <summary>
/// Request para el LLM
/// </summary>
public class LLMRequest
{
    /// <summary>
    /// Mensajes de la conversación
    /// </summary>
    public List<LLMMessage> Messages { get; set; } = new();

    /// <summary>
    /// Temperatura (0-1), controla creatividad
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Máximo de tokens en la respuesta
    /// </summary>
    public int MaxTokens { get; set; } = 500;

    /// <summary>
    /// Modelo a usar (opcional, usa el configurado por defecto si no se especifica)
    /// </summary>
    public string? Model { get; set; }
}
