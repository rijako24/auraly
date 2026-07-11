using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.DTOs;

public class Message
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public TextMessage? Text { get; set; }

    [JsonPropertyName("audio")]
    public AudioMessage? Audio { get; set; }

    [JsonPropertyName("voice")]
    public VoiceMessage? Voice { get; set; }

    [JsonPropertyName("interactive")]
    public InteractiveMessage? Interactive { get; set; }

    [JsonPropertyName("button")]
    public ButtonMessage? Button { get; set; }

    [JsonPropertyName("context")]
    public MessageContext? Context { get; set; }

    [JsonPropertyName("facts")]
    public Dictionary<string, string>? Facts { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public class InteractiveMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("button_reply")]
    public ButtonReply? ButtonReply { get; set; }

    [JsonPropertyName("list_reply")]
    public ListReply? ListReply { get; set; }
}

public class ButtonReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public class ListReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class ButtonMessage
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;
}

public class MessageContext
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}
