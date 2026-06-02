using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IMessageRepository
{
    Task<Message> CreateAsync(Message message);
    Task<IEnumerable<Message>> GetByConversationIdAsync(Guid conversationId);

    Task<IReadOnlyList<Message>> GetRecentByConversationIdAsync(
        Guid conversationId, int limit, CancellationToken ct = default);
    Task<Message?> GetByIdAsync(Guid messageId);

    /// <summary>
    /// Gets paginated messages for a conversation (admin).
    /// </summary>
    Task<(IReadOnlyList<Message> Items, int TotalCount)> GetPagedByConversationIdAsync(
        Guid conversationId, int page, int pageSize, CancellationToken ct = default);
}
