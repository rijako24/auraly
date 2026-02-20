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
}
