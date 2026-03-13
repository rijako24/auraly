using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.DTOs;

public class Message
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;
    public TextMessage? Text { get; set; }
    public AudioMessage? Audio { get; set; }
    public VoiceMessage? Voice { get; set; }
    public string Type { get; set; } = string.Empty;
}
