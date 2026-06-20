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
    public InteractiveMessage? Interactive { get; set; }
    public MessageContext? Context { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class InteractiveMessage
{
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("button_reply")]
    public ButtonReply? ButtonReply { get; set; }
}

public class ButtonReply
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class MessageContext
{
    public string Id { get; set; } = string.Empty;
}
