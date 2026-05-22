namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Una etapa del flujo conversacional declarado por tenant.
/// </summary>
public sealed class AgentFlowStage
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Objetivo narrativo de la etapa (lenguaje natural para el LLM).</summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>Tools sugeridas en esta etapa (preferencia, no obligación).</summary>
    public IReadOnlyList<string> SuggestedTools { get; init; } = [];

    /// <summary>Facts que deben estar presentes para avanzar a la siguiente etapa.</summary>
    public IReadOnlyList<string> AdvanceWhenFacts { get; init; } = [];

    /// <summary>
    /// Si alguno de estos facts cambia después de que la etapa fue completada,
    /// el compositor inyecta un bloque de ATENCIÓN para que el LLM repita acciones dependientes.
    /// Ej.: si service o desired_date cambian luego del checkout, hay que regenerar prepare_checkout.
    /// </summary>
    public IReadOnlyList<string> ReentryOnFactChanged { get; init; } = [];
}
