using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Respuesta estructurada del LLM para extracción de información.
///
/// Mapeada al schema compacto:
/// - "extracted_fields": lista de campos con field_name, value, confidence.
/// - "intentions": 4 intenciones binarias (sin can_check/can_create — los decide el FlowEngine).
/// - "ambiguities": lista de ambigüedades detectadas.
///
/// El LLM ya no retorna conversational_response ni metadata — eso lo genera FASE 5.
/// PropertyNameCaseInsensitive=true en la deserialización tolerará variaciones de casing.
/// </summary>
public class StructuredExtractionResponse
{
    [JsonPropertyName("extracted_fields")]
    public List<ExtractedField> ExtractedFields { get; set; } = new();

    /// <summary>
    /// Intenciones detectadas. El schema nuevo usa "intentions" en vez de "flow_analysis".
    /// Se acepta ambos gracias a PropertyNameCaseInsensitive en el deserializador.
    /// </summary>
    [JsonPropertyName("intentions")]
    public ExtractionIntentions Intentions { get; set; } = new();

    /// <summary>
    /// Compatibilidad retroactiva: si el LLM retorna "flow_analysis" se ignora
    /// (no rompe — solo queda en default). El campo "intentions" es el canónico.
    /// </summary>
    [JsonPropertyName("flow_analysis")]
    public FlowAnalysis? FlowAnalysisLegacy { get; set; }

    [JsonPropertyName("ambiguities")]
    public List<CompactAmbiguity> Ambiguities { get; set; } = new();

    // ─── Metadata calculada por el backend (no depende del LLM) ───────
    public ExtractionMetadata ComputedMetadata =>
        new()
        {
            FieldsExtracted     = ExtractedFields.Count,
            AverageConfidence   = ExtractedFields.Any()
                ? ExtractedFields.Average(f => f.Confidence)
                : 0.0,
            IsComplete          = false,
            NeedsClarification  = Ambiguities.Any()
        };
}

/// <summary>
/// Ambigüedad en formato compacto (schema nuevo: type en vez de ambiguity_type).
/// </summary>
public class CompactAmbiguity
{
    [JsonPropertyName("field_name")]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
