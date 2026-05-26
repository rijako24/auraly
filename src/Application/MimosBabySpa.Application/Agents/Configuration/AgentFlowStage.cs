namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Una etapa del flujo conversacional declarada en el JSON del tenant.
/// El motor (FlowEngine) la interpreta; el LLM solo redacta respuestas.
/// </summary>
public sealed class AgentFlowStage
{
    /// <summary>Identificador único del stage, en snake_case.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Condición de activación del stage.
    /// Si está definida y evalúa false, el motor salta al siguiente stage.
    /// Soporta @fact.X, @pack.X. No soporta @result.X (eso es execute.appliesWhen).
    /// </summary>
    public AgentFlowStageCondition? AppliesWhen { get; init; }

    /// <summary>
    /// Texto literal devuelto al usuario sin pasar por el LLM.
    /// Si está definido, el motor lo devuelve directamente y marca el stage como completado.
    /// </summary>
    public string? Verbatim { get; init; }

    /// <summary>
    /// Instrucción al LLM sobre qué hacer en este stage (lenguaje natural, en inglés).
    /// El motor la incluye en el system prompt. Si null, el LLM usa su criterio basado en el ID.
    /// </summary>
    public string? Ask { get; init; }

    /// <summary>
    /// Tool de solo lectura ejecutada por el motor ANTES de la llamada LLM.
    /// Idempotente: enriquece contexto, no crea ni modifica datos.
    /// </summary>
    public AgentFlowStageLookup? Lookup { get; init; }

    /// <summary>
    /// Tool destructiva ejecutada por el motor cuando se cumplen las condiciones de completedWhen.
    /// </summary>
    public AgentFlowStageExecute? Execute { get; init; }

    /// <summary>
    /// Lista de tools permitidas en este stage. Si está definida, el LLM solo puede ver estas tools.
    /// </summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>
    /// ID del template Handlebars a renderizar con los datos del lookup o execute.
    /// Puede ser un ID literal (p. ej. "availability_slots") o "@result.template_id"
    /// para usar el template_id que devuelve la tool en su rawJson.
    /// </summary>
    public string? Template { get; init; }

    /// <summary>
    /// Presentación del lookup: <see cref="FlowStageLookupPresentation.Verbatim"/> (default) o
    /// <see cref="FlowStageLookupPresentation.LlmCurate"/> (catálogo en prompt; el LLM filtra, p. ej. por edad).
    /// </summary>
    public string? LookupPresentation { get; init; }

    /// <summary>
    /// Facts que este stage espera capturar del usuario.
    /// El LLM los verá en el prompt como campos a extraer.
    /// El stage avanza cuando todos están presentes (si completedWhen=factsCollected).
    /// </summary>
    public IReadOnlyList<string> Collects { get; init; } = [];

    /// <summary>
    /// Criterio de completado del stage. Ver <see cref="StageCompletionCriteria"/>.
    /// </summary>
    public string CompletedWhen { get; init; } = StageCompletionCriteria.Always;
}
