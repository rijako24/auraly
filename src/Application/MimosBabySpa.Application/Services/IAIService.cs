using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IAIService
{
    Task<string> GenerateResponseAsync(Guid businessId, string userMessage, Conversation? conversation, string intent, Lead? lead);
    Task<string> ClassifyIntentAsync(string messageText, Conversation? conversation);
    Task<DTOs.IntentAndContextResult> ClassifyIntentAndExtractContextAsync(Guid businessId, string messageText, Conversation? conversation);
    Task<string> TranscribeAudioAsync(Stream audioStream, string mimeType);
}
