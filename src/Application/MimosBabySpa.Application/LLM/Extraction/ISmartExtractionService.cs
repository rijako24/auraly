using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Servicio de extracción inteligente con validación y fallback.
/// Devuelve <see cref="ExtractionOutput"/> (contrato del pipeline); el orquestador no mapea.
/// </summary>
public interface ISmartExtractionService
{
    /// <summary>
    /// Extrae información del mensaje con validación.
    /// Recibe LoadedBusinessContext precargado (multitenant: por businessId).
    /// </summary>
    Task<ExtractionOutput> ExtractWithValidationAsync(
        string userMessage,
        ConversationState currentState,
        Configuration.LoadedBusinessContext businessContext,
        CancellationToken cancellationToken);
}
