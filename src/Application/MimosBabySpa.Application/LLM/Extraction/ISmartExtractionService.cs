using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Servicio de extracción inteligente con validación y fallback
/// </summary>
public interface ISmartExtractionService
{
    /// <summary>
    /// Extrae información del mensaje con validación.
    /// ✅ Recibe LoadedBusinessContext precargado para evitar cargas redundantes.
    /// </summary>
    Task<ExtractionResult> ExtractWithValidationAsync(
        string userMessage,
        ConversationState currentState,
        Configuration.LoadedBusinessContext businessContext,
        CancellationToken cancellationToken);
}
