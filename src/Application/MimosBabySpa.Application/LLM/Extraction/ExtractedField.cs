using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Campo extraído con confidence score
/// </summary>
public class ExtractedField
{
    [JsonPropertyName("field_name")]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("field_type")]
    public FieldType FieldType { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonPropertyName("source_text")]
    public string? SourceText { get; set; }

    [JsonPropertyName("is_update")]
    public bool IsUpdate { get; set; }
}
