using System.Text.Json;

namespace MimosBabySpa.Application.Models;

/// <summary>
/// Representa una solicitud de ejecución de una herramienta desde OpenAI
/// </summary>
public class ToolCallRequest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonElement? Arguments { get; set; }
}
