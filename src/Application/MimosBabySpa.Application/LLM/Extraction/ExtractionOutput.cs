namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Contrato de salida del pipeline de extracción hacia el orquestador.
///
/// Contiene:
/// - ExtractedFields: campos extraídos con su confidence (solo >= MinConfidence).
/// - Intentions: intenciones detectadas por LLM o regex (solo críticas en Degraded).
/// - Ambiguities: información que el LLM no pudo resolver con seguridad.
/// - Method: LLM (éxito) o Degraded (fallo).
/// - WasSuccessful: false en Degraded. El orquestador decide el mensaje al usuario.
///
/// No contiene can_check/can_create — esos los calcula el FlowEngine.
/// </summary>
public class ExtractionOutput
{
    public List<ExtractedField> ExtractedFields { get; set; } = new();
    public ExtractionIntentions Intentions { get; set; } = new();
    public List<CompactAmbiguity> Ambiguities { get; set; } = new();
    public ExtractionMethod Method { get; set; }
    public bool WasSuccessful { get; set; }
}
