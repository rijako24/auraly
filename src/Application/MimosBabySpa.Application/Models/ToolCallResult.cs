namespace MimosBabySpa.Application.Models;

/// <summary>
/// Representa el resultado de la ejecución de una herramienta
/// </summary>
public class ToolCallResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}
