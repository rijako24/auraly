using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryConversationRepository : IConversationRepository
{
    private readonly List<Conversation> _store = [];

    public Task<Conversation?> GetByUserNumberAsync(string userNumber) =>
        Task.FromResult(_store.FirstOrDefault(c => c.UserNumber == userNumber));

    public Task<Conversation?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber) =>
        Task.FromResult(_store
            .Where(c => c.BusinessId == businessId && c.UserNumber == userNumber)
            .OrderByDescending(c => c.OpenedAt)
            .FirstOrDefault());

    public Task<Conversation?> GetActiveByBusinessIdAndUserNumberAsync(
        Guid businessId, string userNumber, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(c =>
            c.BusinessId == businessId
            && c.UserNumber == userNumber
            && c.Status == ConversationLifecycleStatus.Active));

    public Task<bool> HasClosedConversationsAsync(
        Guid businessId, string userNumber, CancellationToken ct = default) =>
        Task.FromResult(_store.Any(c =>
            c.BusinessId == businessId
            && c.UserNumber == userNumber
            && c.Status == ConversationLifecycleStatus.Closed));

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
        Guid businessId, int page, int pageSize, string? search,
        ConversationLifecycleStatus? status, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
