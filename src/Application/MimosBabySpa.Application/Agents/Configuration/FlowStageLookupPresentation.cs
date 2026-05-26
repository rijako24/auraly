namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Cómo el motor presenta el resultado de <see cref="AgentFlowStageLookup"/> al usuario.
/// </summary>
public static class FlowStageLookupPresentation
{
    /// <summary>Renderiza el template Handlebars y lo exige verbatim en la respuesta (slots, add-ons).</summary>
    public const string Verbatim = "verbatim";

    /// <summary>
    /// Pasa el catálogo como referencia al LLM; el modelo muestra solo lo pertinente (p. ej. planes por edad del bebé).
    /// </summary>
    public const string LlmCurate = "llm_curate";

    public static bool IsLlmCurate(AgentFlowStage? stage) =>
        stage is not null
        && string.Equals(stage.LookupPresentation, LlmCurate, StringComparison.OrdinalIgnoreCase);
}
