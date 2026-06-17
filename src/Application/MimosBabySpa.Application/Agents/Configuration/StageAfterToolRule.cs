using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Regla declarativa que se evalua despues de ejecutar una tool en una etapa.
/// Permite que el flujo persista facts a partir de resultados estructurados sin
/// que el motor conozca el dominio de la tool.
/// </summary>
public sealed class StageAfterToolRule
{
    public string Tool { get; init; } = string.Empty;

    public ToolResultCondition When { get; init; } = new();

    public ToolSetFactAction SetFact { get; init; } = new();

    public Dictionary<string, string> SetFacts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? SendMessageSequence { get; init; }

    public bool SendOncePerConversation { get; init; }
}

public sealed class ToolResultCondition
{
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("equals")]
    public string? Expected { get; init; }

    [JsonPropertyName("notEquals")]
    public string? NotExpected { get; init; }
}

public sealed class ToolSetFactAction
{
    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}
