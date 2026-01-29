namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Resultado de la ejecución de una herramienta
/// </summary>
public class ToolExecutionResult
{
    /// <summary>
    /// Indica si la ejecución fue exitosa
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Mensaje descriptivo del resultado (para el LLM)
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Datos estructurados del resultado (para el sistema)
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>
    /// Excepción si ocurrió un error
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Indica si el estado de la conversación fue modificado
    /// </summary>
    public bool StateModified { get; set; }
}
