using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Extractor de fallback con reglas determinísticas mínimas y genéricas.
///
/// Recibe LoadedBusinessContext (ya cargado) en vez de Guid businessId,
/// eliminando la query adicional a BD que hacía el fallback anterior.
/// Multitenant: los atributos vienen del contexto precargado.
/// </summary>
public interface IFallbackExtractor
{
    Task<StructuredExtractionResponse> ExtractAsync(
        string userMessage,
        ConversationState currentState,
        LoadedBusinessContext businessContext,
        StructuredExtractionResponse? llmAttempt,
        CancellationToken cancellationToken = default);
}
