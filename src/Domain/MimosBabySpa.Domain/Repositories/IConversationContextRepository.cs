using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IConversationContextRepository
{
    Task<ConversationContext?> GetByConversationIdAndFieldAsync(Guid conversationId, string field);
    Task<ConversationContext> CreateOrUpdateAsync(Guid conversationId, string field, string value);
    Task<IEnumerable<ConversationContext>> GetByConversationIdAsync(Guid conversationId);
    Task DeleteByConversationIdAsync(Guid conversationId);
}
