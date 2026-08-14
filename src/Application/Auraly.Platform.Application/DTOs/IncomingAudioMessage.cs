namespace Auraly.Platform.Application.DTOs;

/// <summary>
/// Representa un mensaje de audio entrante de WhatsApp
/// </summary>
public class IncomingAudioMessage
{
    public string UserNumber { get; set; } = string.Empty;
    public string MediaId { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public bool IsVoice { get; set; } // true para voice, false para audio
}
