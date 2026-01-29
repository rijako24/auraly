using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Valor posible en caso de ambigüedad
/// </summary>
public class PossibleValue
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("interpretation")]
    public string Interpretation { get; set; } = string.Empty;
}
