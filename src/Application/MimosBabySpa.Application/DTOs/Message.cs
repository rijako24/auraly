namespace MimosBabySpa.Application.DTOs;

public class Message
{
    public string From { get; set; } = string.Empty;
    public TextMessage? Text { get; set; }
    public AudioMessage? Audio { get; set; }
    public VoiceMessage? Voice { get; set; }
    public string Type { get; set; } = string.Empty;
}
