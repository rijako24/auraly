using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

public enum AmbiguitySeverity
{
    [JsonPropertyName("low")]
    Low,
    
    [JsonPropertyName("medium")]
    Medium,
    
    [JsonPropertyName("high")]
    High
}
