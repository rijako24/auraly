using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Servicio de extracción inteligente con validación y fallback.
/// Devuelve <see cref="ExtractionOutput"/> (contrato del pipeline); el orquestador no mapea.
/// Usa historial conversacional para interpretar respuestas en contexto (ej: "2" como respuesta a "¿Para cuántos bebés?").
/// </summary>
public interface ISmartExtractionService
{
    /// <summary>
    /// Extrae información del mensaje con validación.
    /// Recibe LoadedBusinessContext precargado (multitenant: por businessId).
    /// El historial reciente permite al LLM interpretar respuestas cortas en contexto.
    /// </summary>
    Task<ExtractionOutput> ExtractWithValidationAsync(
        string userMessage,
        ConversationState currentState,
        Configuration.LoadedBusinessContext businessContext,
        IReadOnlyList<Message> recentHistory,
        CancellationToken cancellationToken);
}
