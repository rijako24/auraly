using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryMessageRepository : IMessageRepository
{
    private readonly List<Message> _store = [];

    public Task<Message> CreateAsync(Message message)
    {
        _store.Add(message);
        return Task.FromResult(message);
    }

    public Task<IEnumerable<Message>> GetByConversationIdAsync(Guid conversationId) =>
        Task.FromResult(_store.Where(m => m.ConversationId == conversationId));

    public Task<Message?> GetByIdAsync(Guid messageId) =>
        Task.FromResult(_store.FirstOrDefault(m => m.MessageId == messageId));
}
