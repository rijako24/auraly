using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Agents.Orchestration;

public sealed class FlowLlmRequest
{
    public AgentConfig Config { get; init; } = null!;

    /// <summary>Stage activo en esta iteración del bucle del turno.</summary>
    public AgentFlowStage? Stage { get; init; }

    public string UserMessage { get; init; } = string.Empty;

    /// <summary>Keys que este stage debe capturar (collects). Prioridad en el prompt.</summary>
    public IReadOnlyList<string> StageCollects { get; init; } = [];

    public IReadOnlyDictionary<string, string> KnownFacts { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MimosBabySpa.Application.LLM.ChatToolDefinition> AvailableTools { get; init; } = [];
    public IReadOnlyList<MimosBabySpa.Application.LLM.ChatMessage> ExtraMessages { get; init; } = [];

    /// <summary>Datos del lookup ya ejecutado por el motor para este stage.</summary>
    public FlowToolResult? LookupResult { get; init; }

    /// <summary>Hint interno cuando el motor omitió el lookup por args de facts faltantes.</summary>
    public string? LookupOmittedHint { get; init; }

    /// <summary>Template Handlebars renderizado. Si no null, el LLM lo incluye verbatim.</summary>
    public string? RenderedTemplate { get; init; }

    public IReadOnlyList<Message> History { get; init; } = [];
}
