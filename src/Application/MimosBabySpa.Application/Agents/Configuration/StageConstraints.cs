namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Restricciones de comportamiento conversacional para una etapa del flujo.
/// Declarativas por tenant — el compositor las traduce a instrucciones para el LLM.
/// </summary>
public sealed class StageConstraints
{
    /// <summary>
    /// Máximo de preguntas que el LLM puede hacer en un solo turno.
    /// 0 = solo responder (ej. turno de saludo CompletesOnEnter).
    /// 1 = una sola pregunta enfocada (recomendado para add-ons, scheduling).
    /// </summary>
    public int? MaxQuestions { get; init; }

    /// <summary>
    /// Estilo de presentación para etapas de oferta suave.
    /// "soft_offer"  → presenta opciones sin presionar; una sola pregunta cerrada.
    /// "direct_ask"  → pregunta directa y concisa (por defecto implícito).
    /// </summary>
    public string? PresentationMode { get; init; }
}
