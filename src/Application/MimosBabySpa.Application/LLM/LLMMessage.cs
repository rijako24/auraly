namespace MimosBabySpa.Application.LLM;

/// <summary>
/// Mensaje en la conversación con el LLM
/// </summary>
public class LLMMessage
{
    /// <summary>
    /// Rol del mensaje
    /// </summary>
    public LLMRole Role { get; set; }

    /// <summary>
    /// Contenido del mensaje
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
