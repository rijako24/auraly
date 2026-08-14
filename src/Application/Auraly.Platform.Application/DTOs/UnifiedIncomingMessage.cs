namespace Auraly.Platform.Application.DTOs;

/// <summary>
/// Representa un mensaje entrante unificado de WhatsApp (puede ser texto o audio)
/// </summary>
public class UnifiedIncomingMessage
{
    public string UserNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    
    // Si es mensaje de texto
    public string? MessageText { get; set; }
    
    // Si es mensaje de audio
    public IncomingAudioMessage? AudioMessage { get; set; }
    
    public bool IsText => MessageText != null;
    public bool IsAudio => AudioMessage != null;
}
