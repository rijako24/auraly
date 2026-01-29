using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Extractor de fallback con reglas determinísticas mínimas genéricas y multi-tenant
/// </summary>
public interface IFallbackExtractor
{
    Task<StructuredExtractionResponse> ExtractAsync(
        string userMessage,
        ConversationState currentState,
        Guid businessId,
        StructuredExtractionResponse? llmAttempt,
        CancellationToken cancellationToken = default);
}
