using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

public enum FieldType
{
    [JsonPropertyName("text")]
    Text,
    
    [JsonPropertyName("number")]
    Number,
    
    [JsonPropertyName("date")]
    Date,
    
    [JsonPropertyName("time")]
    Time,
    
    [JsonPropertyName("email")]
    Email,
    
    [JsonPropertyName("phone")]
    Phone,
    
    [JsonPropertyName("service")]
    Service
}
