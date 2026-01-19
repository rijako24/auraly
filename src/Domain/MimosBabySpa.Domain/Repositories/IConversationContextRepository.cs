using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IConversationContextRepository
{
    Task<IEnumerable<ConversationContext>> GetByConversationIdAsync(Guid conversationId);
    Task<ConversationContext> CreateAsync(Guid conversationId, string context);
    Task<int> CreateBatchAsync(Guid conversationId, IEnumerable<string> contexts);
    Task DeleteAsync(Guid conversationContextId);
    Task DeleteByConversationIdAsync(Guid conversationId);
    Task<bool> ExistsAsync(Guid conversationId, string context);
}
