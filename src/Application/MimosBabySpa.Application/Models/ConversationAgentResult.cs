namespace MimosBabySpa.Application.Models;

/// <summary>
/// Resultado de la ejecución del agente conversacional
/// </summary>
public class ConversationAgentResult
{
    public string Response { get; set; } = string.Empty;
    public List<string> ExtractedContext { get; set; } = new();
    public bool RequiresToolExecution { get; set; }
    public List<ToolCallRequest> ToolCalls { get; set; } = new();
}
