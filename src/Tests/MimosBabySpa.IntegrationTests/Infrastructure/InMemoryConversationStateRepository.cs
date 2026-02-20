using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryConversationStateRepository : IConversationStateRepository
{
    private readonly List<ConversationStateEntity> _store = [];

    public Task<ConversationStateEntity?> GetByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.FirstOrDefault(s => s.ConversationId == conversationId));

    public Task SaveAsync(ConversationStateEntity entity, CancellationToken cancellationToken = default)
    {
        var idx = _store.FindIndex(s => s.ConversationId == entity.ConversationId);
        if (idx >= 0) _store[idx] = entity;
        else _store.Add(entity);
        return Task.CompletedTask;
    }
}
