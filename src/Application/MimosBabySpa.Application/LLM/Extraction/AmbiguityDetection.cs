using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Detección de ambigüedad
/// </summary>
public class AmbiguityDetection
{
    [JsonPropertyName("field_name")]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("ambiguity_type")]
    public AmbiguityType Type { get; set; }

    [JsonPropertyName("possible_values")]
    public List<PossibleValue> PossibleValues { get; set; } = new();

    [JsonPropertyName("ambiguous_text")]
    public string AmbiguousText { get; set; } = string.Empty;

    [JsonPropertyName("clarification_question")]
    public string ClarificationQuestion { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public AmbiguitySeverity Severity { get; set; }
}
