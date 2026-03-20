using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryConversationRepository : IConversationRepository
{
    private readonly List<Conversation> _store = [];

    public Task<Conversation?> GetByUserNumberAsync(string userNumber) =>
        Task.FromResult(_store.FirstOrDefault(c => c.UserNumber == userNumber));

    public Task<Conversation?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber) =>
        Task.FromResult(_store.FirstOrDefault(c =>
            c.BusinessId == businessId && c.UserNumber == userNumber));

    public Task<Conversation> CreateAsync(Conversation conversation)
    {
        _store.Add(conversation);
        return Task.FromResult(conversation);
    }

    public Task<Conversation> UpdateAsync(Conversation conversation)
    {
        var idx = _store.FindIndex(c => c.ConversationId == conversation.ConversationId);
        if (idx >= 0) _store[idx] = conversation;
        else _store.Add(conversation);
        return Task.FromResult(conversation);
    }

    public Task<Conversation?> GetByIdAsync(Guid conversationId) =>
        Task.FromResult(_store.FirstOrDefault(c => c.ConversationId == conversationId));

    public Task<(IReadOnlyList<Conversation> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        var query = _store.Where(c => c.BusinessId == businessId).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.UserNumber.ToLowerInvariant().Contains(term) ||
                (c.CustomerName != null && c.CustomerName.ToLowerInvariant().Contains(term)) ||
                (c.LastMessage != null && c.LastMessage.ToLowerInvariant().Contains(term)));
        }

        var list = query.OrderByDescending(c => c.Timestamp).ToList();
        var total = list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult<(IReadOnlyList<Conversation> Items, int TotalCount)>((items, total));
    }
}
