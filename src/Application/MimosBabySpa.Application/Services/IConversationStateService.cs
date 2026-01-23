using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para gestionar estados explícitos de conversación.
/// Evita saltos de flujo y confirmaciones prematuras.
/// </summary>
public interface IConversationStateService
{
    /// <summary>
    /// Obtiene el estado actual de una conversación
    /// </summary>
    Task<ConversationState> GetStateAsync(Guid conversationId);
    
    /// <summary>
    /// Actualiza el estado de una conversación
    /// </summary>
    Task SetStateAsync(Guid conversationId, ConversationState newState);
    
    /// <summary>
    /// Verifica si una transición de estado es válida
    /// </summary>
    bool IsValidTransition(ConversationState from, ConversationState to);
}
