using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Metadata sobre la extracción
/// </summary>
public class ExtractionMetadata
{
    [JsonPropertyName("fields_extracted")]
    public int FieldsExtracted { get; set; }

    [JsonPropertyName("average_confidence")]
    public double AverageConfidence { get; set; }

    [JsonPropertyName("is_complete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("needs_clarification")]
    public bool NeedsClarification { get; set; }

    [JsonPropertyName("detected_language")]
    public string DetectedLanguage { get; set; } = "es";
}
