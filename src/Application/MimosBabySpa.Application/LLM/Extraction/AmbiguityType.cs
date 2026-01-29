using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

[JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum AmbiguityType
{
    [JsonPropertyName("multiple_interpretations")]
    MultipleInterpretations,
    
    [JsonPropertyName("unclear_reference")]
    UnclearReference,
    
    [JsonPropertyName("incomplete_information")]
    IncompleteInformation,
    
    [JsonPropertyName("incomplete")]
    Incomplete, // Alias para incomplete_information
    
    [JsonPropertyName("vague_temporal")]
    VagueTemporal,
    
    [JsonPropertyName("temporal")]
    Temporal, // Alias para vague_temporal
    
    [JsonPropertyName("referential")]
    Referential, // Alias para unclear_reference
    
    [JsonPropertyName("multiple_values")]
    MultipleValues // Alias para multiple_interpretations
}
