namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Una etapa del flujo conversacional declarado por tenant.
/// </summary>
public sealed class AgentFlowStage
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Objetivo narrativo de la etapa (lenguaje natural para el LLM).</summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>
    /// Whitelist de tools permitidas en esta etapa. Vacío = sin restricción por etapa.
    /// Cuando está definido, el gate bloquea cualquier otra tool con la instrucción de la etapa.
    /// </summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>
    /// Orientación concreta para el LLM en esta etapa (acción y pregunta cerrada esperada).
    /// Las variantes pueden sobreescribir con su propio hint.
    /// </summary>
    public string? Hint { get; init; }

    /// <summary>Facts que deben estar presentes para avanzar a la siguiente etapa.</summary>
    public IReadOnlyList<string> AdvanceWhenFacts { get; init; } = [];

    /// <summary>
    /// Si alguno de estos facts cambia después de que la etapa fue completada,
    /// el compositor inyecta un bloque de ATENCIÓN para que el LLM repita acciones dependientes.
    /// </summary>
    public IReadOnlyList<string> ReentryOnFactChanged { get; init; } = [];

    /// <summary>
    /// Restricciones de comportamiento conversacional. Declarativas por tenant.
    /// El compositor las traduce en instrucciones para el LLM.
    /// </summary>
    public StageConstraints? Constraints { get; init; }

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
    /// Variantes de la etapa por engagement context.
    /// Keys: "firstEver" | "returningCustomer" | "continuingSession".
    /// Si la etapa tiene Variants y el engagement actual NO está en el dict,
    /// la etapa se salta automáticamente (no aplica a este tipo de cliente).
    /// </summary>
    public Dictionary<string, AgentFlowStageVariant> Variants { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}
