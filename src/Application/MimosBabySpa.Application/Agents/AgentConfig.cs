using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Configuración de un agente cargada desde BD por turno (con caché).
/// Fuente: Agents.SettingsJson (persona, flow, guards, etc.) con fallback legacy a SystemPromptMarkdown.
/// </summary>
public sealed class AgentConfig
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Identidad y tono del agente (SettingsJson → persona).</summary>
    public string Persona { get; init; } = string.Empty;

    /// <summary>Políticas operativas en markdown (SettingsJson → policies).</summary>
    public string Policies { get; init; } = string.Empty;

    /// <summary>Flujo conversacional estructurado por etapas.</summary>
    public AgentFlowDefinition Flow { get; init; } = new();

    /// <summary>Schema de facts rastreados por este agente.</summary>
    public IReadOnlyList<FactSchemaEntry> FactSchema { get; init; } = [];

    /// <summary>Precondiciones declarativas por capability:<id> (SettingsJson -> guards).</summary>
    public IReadOnlyDictionary<string, GuardDefinition> Guards { get; init; }
        = new Dictionary<string, GuardDefinition>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Plantillas de mensaje override por templateId.</summary>
    public IReadOnlyDictionary<string, string> Templates { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Legacy: Agents.SystemPromptMarkdown. Usado solo si Persona está vacía.</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>Contenido base del prompt: Persona + Policies, o SystemPrompt legacy.</summary>
    public string BasePrompt =>
        !string.IsNullOrWhiteSpace(Persona)
            ? string.IsNullOrWhiteSpace(Policies)
                ? Persona.Trim()
                : $"{Persona.Trim()}{Environment.NewLine}{Environment.NewLine}{Policies.Trim()}"
            : SystemPrompt.Trim();

    /// <summary>
    /// Frases que disparan escalación inmediata a humano sin pasar por el LLM.
    /// Evaluación case-insensitive, por contención parcial.
    /// Vacío = kill-switch desactivado.
    /// </summary>
    public IReadOnlyList<string> KillSwitchPhrases { get; init; } = [];

    /// <summary>Deployment o model name de Azure OpenAI.</summary>
    public string Model { get; init; } = string.Empty;

    public float Temperature { get; init; } = 0.7f;

    /// <summary>Máximo de iteraciones de tool calling por turno (anti-loop).</summary>
    public int MaxToolIterations { get; init; } = 6;

    /// <summary>Máximo de mensajes del historial enviados al LLM por turno.</summary>
    public int HistoryWindowSize { get; init; } = 20;

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

    /// <summary>
    /// Catálogo de secuencias outbound nombradas (texto + adjuntos).
    /// Fuente: SettingsJson → messageSequences.
    /// </summary>
    public MessageSequenceCatalog MessageSequences { get; init; } = new();

    /// <summary>
    /// Disparadores de secuencias por webhook (p. ej. Wompi).
    /// Fuente: SettingsJson → webhooks.
    /// </summary>
    public WebhookDefinitions Webhooks { get; init; } = new();

    public CheckoutDefinitions Checkout { get; init; } = new();
}
