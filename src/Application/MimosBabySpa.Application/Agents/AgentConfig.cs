using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Commerce;

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

    public ConversationalFlowLanguage FlowLanguage => Flow.Language;

    /// <summary>Acciones transversales disponibles sin depender de la etapa activa.</summary>
    public IReadOnlyList<AgentGlobalAction> GlobalActions { get; init; } = [];

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
    /// Catálogo de secuencias outbound nombradas (texto + adjuntos).
    /// Fuente: SettingsJson → messageSequences.
    /// </summary>
    public MessageSequenceCatalog MessageSequences { get; init; } = new();

    /// <summary>
    /// Disparadores de secuencias por webhook (p. ej. Wompi).
    /// Fuente: SettingsJson → webhooks.
    /// </summary>
    public WebhookDefinitions Webhooks { get; init; } = new();

    /// <summary>
    /// Notificaciones internas disparadas por eventos del motor.
    /// Fuente: SettingsJson -> notifications.
    /// </summary>
    public NotificationDefinitions Notifications { get; init; } = new();

    /// <summary>
    /// Configuracion unificada de escalaciones.
    /// Fuente: SettingsJson -> escalations.
    /// </summary>
    public EscalationDefinitions Escalations { get; init; } = new();

    public ReservationAutomationDefinitions ReservationAutomations { get; init; } = new();

    public ReservationManagementDefinitions ReservationManagement { get; init; } = new();

    public CheckoutDefinitions Checkout { get; init; } = new();

    public CommerceConfig Commerce { get; init; } = new();

    public OperatingHoursDefinitions OperatingHours { get; init; } = new();
}
