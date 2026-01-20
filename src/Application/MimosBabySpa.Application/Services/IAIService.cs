using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IAIService
{
    Task<string> GenerateResponseAsync(Guid businessId, string userMessage, Conversation? conversation, string intent, Lead? lead);
    Task<string> TranscribeAudioAsync(Stream audioStream, string mimeType);
    Task<string> ProcessCustomPromptAsync(string systemPrompt, string userPrompt, bool jsonResponse = false, float temperature = 0.3f, int maxTokens = 400);
}
