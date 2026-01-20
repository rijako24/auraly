using System.Text.Json;

namespace MimosBabySpa.Application.Models;

/// <summary>
/// Define una herramienta disponible para OpenAI Function Calling
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonDocument ParametersSchema { get; set; } = null!;
}
