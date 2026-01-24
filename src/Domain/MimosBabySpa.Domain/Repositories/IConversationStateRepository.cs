using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Domain.Repositories;

/// <summary>
/// Repositorio para gestionar el estado de conversación almacenado como JSON.
/// </summary>
public interface IConversationStateRepository
{
    /// <summary>
    /// Obtiene el estado de conversación. Si no existe, retorna un estado vacío.
    /// </summary>
    Task<ConversationState> GetAsync(Guid conversationId);

    /// <summary>
    /// Guarda el estado de conversación. Si no existe, lo crea. Si existe, lo actualiza.
    /// </summary>
    Task SaveAsync(Guid conversationId, Guid businessId, ConversationState state);

    /// <summary>
    /// Elimina el estado de conversación (útil para reset).
    /// </summary>
    Task DeleteAsync(Guid conversationId);

    /// <summary>
    /// Verifica si existe un estado para la conversación.
    /// </summary>
    Task<bool> ExistsAsync(Guid conversationId);
}
