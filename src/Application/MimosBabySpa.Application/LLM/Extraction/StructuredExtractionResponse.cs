using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Respuesta estructurada del LLM con extracción de información
/// </summary>
public class StructuredExtractionResponse
{
    [JsonPropertyName("extracted_fields")]
    public List<ExtractedField> ExtractedFields { get; set; } = new();

    [JsonPropertyName("conversational_response")]
    public string ConversationalResponse { get; set; } = string.Empty;

    [JsonPropertyName("flow_analysis")]
    public FlowAnalysis FlowAnalysis { get; set; } = new();

    [JsonPropertyName("ambiguities")]
    public List<AmbiguityDetection> Ambiguities { get; set; } = new();

    [JsonPropertyName("metadata")]
    public ExtractionMetadata Metadata { get; set; } = new();
}
