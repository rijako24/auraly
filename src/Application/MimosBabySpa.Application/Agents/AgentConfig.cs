using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Configuracion de un agente cargada desde BD por turno (con cache).
/// Fuente ?nica: Agents.SettingsJson compilado antes de activarse.
/// </summary>
public sealed class AgentConfig
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Identidad y tono del agente (SettingsJson -> persona).</summary>
    public string Persona { get; init; } = string.Empty;

    /// <summary>Politicas operativas en markdown (SettingsJson -> policies).</summary>
    public string Policies { get; init; } = string.Empty;

    /// <summary>Flows conversacionales compilados para este agente.</summary>
    public IReadOnlyList<AgentFlowDefinition> Flows { get; init; } = [];

    /// <summary>Acciones transversales disponibles sin depender de la etapa activa.</summary>
    public IReadOnlyList<AgentGlobalAction> GlobalActions { get; init; } = [];

    /// <summary>Schema de facts rastreados por este agente.</summary>
    public IReadOnlyList<FactSchemaEntry> FactSchema { get; init; } = [];

    /// <summary>Plantillas de mensaje override por templateId.</summary>
    public IReadOnlyDictionary<string, string> Templates { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Politica configurable de apertura de una nueva solicitud.</summary>
    public ConversationOpeningDefinitions ConversationOpening { get; init; } = new();
    public FailureResponseDefinitions FailureResponses { get; init; } = new();

    /// <summary>Policy for resuming a conversation that is waiting for the customer.</summary>
    public ConversationFollowUpDefinitions ConversationFollowUp { get; init; } = new();

    /// <summary>Contenido de estilo para el renderer; nunca contiene reglas de ejecuci?n.</summary>
    public string BasePrompt =>
        string.Join($"{Environment.NewLine}{Environment.NewLine}",
            new[] { Persona.Trim(), Policies.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>Deployment o model name de Azure OpenAI.</summary>
    public string Model { get; init; } = string.Empty;

    public float Temperature { get; init; } = 0.7f;

    /// <summary>Maximo de mensajes del historial enviados al LLM por turno.</summary>
    public int HistoryWindowSize { get; init; } = 20;

    /// <summary>Mensajes inmediatamente anteriores enviados al extractor semantico; el renderer conserva HistoryWindowSize.</summary>
    public int ExtractorHistoryWindowSize { get; init; } = 2;

    /// <summary>
    /// Catalogo de secuencias outbound nombradas (texto + adjuntos).
    /// Fuente: SettingsJson -> messageSequences.
    /// </summary>
    public MessageSequenceCatalog MessageSequences { get; init; } = new();

    /// <summary>
    /// Disparadores de secuencias por webhook (p. ej. Wompi).
    /// Fuente: SettingsJson -> webhooks.
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

    /// <summary>
    /// Acciones deterministas disparadas por payloads interactivos scope:outcome:sourceId.
    /// Fuente: SettingsJson -> interactiveActions.
    /// </summary>
    public InteractiveActionDefinitions InteractiveActions { get; init; } = new();

    public ReservationManagementDefinitions ReservationManagement { get; init; } = new();

    public CheckoutDefinitions Checkout { get; init; } = new();

    public CommerceConfig Commerce { get; init; } = new();

    public OperatingHoursDefinitions OperatingHours { get; init; } = new();
}

public sealed class ConversationOpeningDefinitions
{
    public bool Enabled { get; init; }
    public string Guidance { get; init; } = string.Empty;
    public bool AllowQuestions { get; init; }
}

public sealed class FailureResponseDefinitions
{
    public string LlmUnavailable { get; init; } =
        "Lo siento, estoy experimentando problemas temporales. Por favor, intenta de nuevo en un momento.";
}
