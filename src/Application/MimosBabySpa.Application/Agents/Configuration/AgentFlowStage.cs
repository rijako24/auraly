namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Una etapa del flujo conversacional declarado por tenant.
/// </summary>
public sealed class AgentFlowStage
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Nombre corto y amigable de la etapa para mostrar en interfaces administrativas.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Objetivo narrativo de la etapa (lenguaje natural para el LLM).</summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>Pregunta conversacional sugerida cuando la etapa necesita input del cliente.</summary>
    public string? Ask { get; init; }

    /// <summary>Datos de negocio que la etapa puede recoger sin bloquear si no son necesarios.</summary>
    public IReadOnlyList<string> Collect { get; init; } = [];

    /// <summary>Acciones semanticas del flow.language.actions permitidas en esta etapa.</summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];

    /// <summary>Orientacion conversacional para conservar el comportamiento hablado del agente.</summary>
    public string? ConversationGuidance { get; init; }

    public string? OnSuccess { get; init; }

    public string? OnProblem { get; init; }

    /// <summary>
    /// </summary>

    /// <summary>Facts que deben estar presentes para avanzar a la siguiente etapa.</summary>
    public IReadOnlyList<string> AdvanceWhenFacts { get; init; } = [];

    /// <summary>
    /// Si alguno de estos facts cambia después de que la etapa fue completada,
    /// el compositor inyecta un bloque de ATENCIÓN para que el LLM repita acciones dependientes.
    /// </summary>
    public IReadOnlyList<string> ReentryOnFactChanged { get; init; } = [];

    /// <summary>
    /// Expresión de facts que, si se cumple, permite saltar esta etapa aunque sus
    /// AdvanceWhenFacts no estén completos (ej. el cliente dio fecha/hora antes de elegir add-ons).
    /// Sintaxis: fact keys separados por "&&" (todos deben estar presentes).
    /// Ej.: "desired_date &amp;&amp; desired_time"
    /// </summary>
    public string? SkipWhen { get; init; }

    /// <summary>
    /// Map fact → valor a grabar automáticamente cuando la etapa se salta por SkipWhen.
    /// Garantiza consistencia del estado sin intervención del LLM.
    /// Ej.: { "add_ons": "ninguno" }
    /// </summary>
    public Dictionary<string, string> AutoSetOnSkip { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reglas declarativas evaluadas despues de tool calls en esta etapa.
    /// Ej.: si get_compatible_add_ons devuelve data.count == 0, setear add_ons=ninguno.
    /// </summary>
    public IReadOnlyList<StageAfterToolRule> AfterTool { get; init; } = [];

    /// <summary>
    /// Variantes de la etapa por engagement context.
    /// Keys: "firstEver" | "returningCustomer" | "continuingSession".
    /// Si la etapa tiene Variants y el engagement actual NO está en el dict,
    /// la etapa se salta automáticamente (no aplica a este tipo de cliente).
    /// </summary>
    public Dictionary<string, AgentFlowStageVariant> Variants { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}
