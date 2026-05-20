namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio de transcripción de audio. La generación de respuestas chat
/// fue migrada a AgentConversationService + IChatClient.
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Transcribe un stream de audio (voz de WhatsApp) a texto usando Whisper.
    /// </summary>
    Task<string> TranscribeAudioAsync(Stream audioStream, string mimeType);
}
