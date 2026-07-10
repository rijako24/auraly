using System.Text.Json;

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


    /// <summary>Datos de negocio que la etapa puede recoger sin bloquear si no son necesarios.</summary>
    public IReadOnlyList<string> Collect { get; init; } = [];

    /// <summary>Nombres exactos de tools permitidas en esta etapa.</summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];

    /// <summary>Acciones exactas de tool que el motor puede ejecutar al entrar a la etapa si se cumplen condiciones declarativas.</summary>
    public IReadOnlyList<StageEntryAction> EntryActions { get; init; } = [];

    /// <summary>Orientacion conversacional para conservar el comportamiento hablado del agente.</summary>
    public string? ConversationGuidance { get; init; }

    public string? OnSuccess { get; init; }

    public string? OnProblem { get; init; }

    /// <summary>
    /// </summary>

    /// <summary>Facts que deben estar presentes para avanzar a la siguiente etapa.</summary>
    public IReadOnlyList<string> AdvanceWhenFacts { get; init; } = [];

    /// <summary>
    /// Si alguno de estos facts cambia despues de que la etapa fue completada,
    /// el compositor inyecta un bloque de ATENCION para que el LLM repita acciones dependientes.
    /// </summary>
    public IReadOnlyList<string> ReentryOnFactChanged { get; init; } = [];

    /// <summary>
    /// Expresion de facts que, si se cumple, permite saltar esta etapa aunque sus
    /// AdvanceWhenFacts no esten completos (ej. el cliente dio fecha/hora antes de elegir add-ons).
    /// Sintaxis: fact keys separados por "&&" (todos deben estar presentes).
    /// Ej.: "desired_date &amp;&amp; desired_time"
    /// </summary>
    public string? SkipWhen { get; init; }

    /// <summary>
    /// Map fact -> valor a grabar automaticamente cuando la etapa se salta por SkipWhen.
    /// Garantiza consistencia del estado sin intervencion del LLM.
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
    /// Si la etapa tiene Variants y el engagement actual NO esta en el dict,
    /// la etapa se salta automaticamente (no aplica a este tipo de cliente).
    /// </summary>
    public Dictionary<string, AgentFlowStageVariant> Variants { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class StageEntryAction
{
    public string Tool { get; init; } = string.Empty;
    public Dictionary<string, JsonElement> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public StageEntryActionCondition When { get; init; } = new();
}

public sealed class StageEntryActionCondition
{
    public IReadOnlyList<string> RequiredFacts { get; init; } = [];
    public IReadOnlyList<string> MissingFacts { get; init; } = [];
    public IReadOnlyList<string> MissingVerifications { get; init; } = [];
    public IReadOnlyList<StageEntryMessageMatch> MessageMatches { get; init; } = [];
}

public sealed class StageEntryMessageMatch
{
    public IReadOnlyList<string> AnyOf { get; init; } = [];
}
