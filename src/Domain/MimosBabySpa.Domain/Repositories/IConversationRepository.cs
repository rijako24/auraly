using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IConversationRepository
{
    Task<Conversation?> GetByUserNumberAsync(string userNumber);
    Task<Conversation?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber);
    Task<Conversation?> GetActiveByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber, CancellationToken ct = default);
    Task<bool> HasClosedConversationsAsync(Guid businessId, string userNumber, CancellationToken ct = default);
    Task<Conversation> CreateAsync(Conversation conversation);
    Task<Conversation> UpdateAsync(Conversation conversation);
    Task<Conversation?> GetByIdAsync(Guid conversationId);

    Task<(IReadOnlyList<Conversation> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null,
        ConversationLifecycleStatus? status = null, CancellationToken ct = default,
        Guid? agentId = null);
}
