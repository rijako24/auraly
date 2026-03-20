using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IConversationRepository
{
    Task<Conversation?> GetByUserNumberAsync(string userNumber);
    Task<Conversation?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber);
    Task<Conversation> CreateAsync(Conversation conversation);
    Task<Conversation> UpdateAsync(Conversation conversation);
    Task<Conversation?> GetByIdAsync(Guid conversationId);

    /// <summary>
    /// Gets paginated conversations for admin dashboard.
    /// </summary>
    Task<(IReadOnlyList<Conversation> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default);
}
