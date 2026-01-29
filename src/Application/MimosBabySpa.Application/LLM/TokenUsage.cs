namespace MimosBabySpa.Application.LLM;

/// <summary>
/// Información de uso de tokens
/// </summary>
public class TokenUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
