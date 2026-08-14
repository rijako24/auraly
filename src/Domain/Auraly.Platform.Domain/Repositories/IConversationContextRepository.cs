using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IConversationContextRepository
{
    Task<ConversationContext?> GetByConversationIdAndFieldAsync(Guid conversationId, string field);
    Task<ConversationContext> CreateOrUpdateAsync(Guid conversationId, string field, string value);
    Task<IEnumerable<ConversationContext>> GetByConversationIdAsync(Guid conversationId);
    Task DeleteFieldsAsync(Guid conversationId, IReadOnlyCollection<string> fields, CancellationToken ct = default);
    Task DeleteByConversationIdAsync(Guid conversationId);
}
