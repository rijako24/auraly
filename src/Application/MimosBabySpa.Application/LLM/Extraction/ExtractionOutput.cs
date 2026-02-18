namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Contrato de salida del pipeline de extracción hacia el orquestador.
///
/// Contiene:
/// - ExtractedFields: campos extraídos con su confidence (solo >= MinConfidence).
/// - Intentions: las 4 intenciones que puede detectar el LLM del texto.
/// - Ambiguities: información que el LLM no pudo resolver con seguridad.
/// - Method: cómo se obtuvo la extracción (LLM / Fallback / Emergency).
/// - WasSuccessful: false solo en emergency (error total del pipeline).
/// - ConversationalResponseSuggestion: solo para emergency fallback.
///
/// No contiene can_check/can_create — esos los calcula el FlowEngine.
/// </summary>
public class ExtractionOutput
{
    public List<ExtractedField>  ExtractedFields              { get; set; } = new();
    public ExtractionIntentions  Intentions                   { get; set; } = new();
    public List<CompactAmbiguity> Ambiguities                 { get; set; } = new();
    public ExtractionMethod      Method                       { get; set; }
    public bool                  WasSuccessful                { get; set; }

    /// <summary>
    /// Solo se usa para la respuesta de emergencia (cuando WasSuccessful=false).
    /// En el flujo normal la respuesta la genera FASE 5.
    /// </summary>
    public string ConversationalResponseSuggestion { get; set; } = string.Empty;
}
