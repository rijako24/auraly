namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Configuración de un agente cargada desde BD por turno (con caché).
/// Lee: Agents.SystemPromptMarkdown + Agents.SettingsJson.
/// </summary>
public sealed class AgentConfig
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>System prompt del agente (Agents.SystemPromptMarkdown).</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Plantilla sugerida para el primer turno (SettingsJson → messages.firstTurnGreetingHint).
    /// El orquestador la inyecta como hint; el LLM la adapta al mensaje del cliente.
    /// </summary>
    public string? FirstTurnGreetingHint { get; init; }

    /// <summary>Deployment o model name de Azure OpenAI.</summary>
    public string Model { get; init; } = string.Empty;

    public float Temperature { get; init; } = 0.7f;

    /// <summary>Máximo de iteraciones de tool calling por turno (anti-loop).</summary>
    public int MaxToolIterations { get; init; } = 6;

    /// <summary>
    /// Nombres de tools habilitadas para este agente.
    /// El registry filtra el set completo antes de enviar al modelo.
    /// </summary>
    public IReadOnlyList<string> EnabledToolNames { get; init; } = [];

    /// <summary>
    /// Umbral de tool errors consecutivos antes de auto-escalar a humano.
    /// Default 3 (configurable por agente en SettingsJson).
    /// </summary>
    public int ConsecutiveErrorEscalationThreshold { get; init; } = 3;

    /// <summary>
    /// Contactos WhatsApp a notificar en escalaciones (e.g. ["+573001234567"]).
    /// Leídos de Agent.SettingsJson → escalation.contacts[].
    /// </summary>
    public IReadOnlyList<string> EscalationContacts { get; init; } = [];
}
