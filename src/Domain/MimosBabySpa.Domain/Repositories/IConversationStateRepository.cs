using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

/// <summary>
/// Repositorio para persistir el estado de conversación
/// </summary>
public interface IConversationStateRepository
{
    /// <summary>
    /// Obtiene el estado de conversación por ConversationId
    /// </summary>
    Task<ConversationStateEntity?> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda o actualiza el estado de conversación
    /// </summary>
    Task SaveAsync(ConversationStateEntity entity, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetDueFollowUpConversationIdsAsync(
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken = default);
}
