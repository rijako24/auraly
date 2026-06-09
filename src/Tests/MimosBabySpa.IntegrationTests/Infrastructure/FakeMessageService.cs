using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// FakeMessageService: saves messages via InMemoryMessageRepository.
/// </summary>
public class FakeMessageService : IMessageService
{
    private readonly IMessageRepository _messages;

    public FakeMessageService(IMessageRepository messages)
    {
        _messages = messages;
    }

    public async Task<Message> SaveMessageAsync(
        Guid conversationId, string sender, string messageText)
    {
        var msg = new Message
        {
            MessageId      = Guid.NewGuid(),
            ConversationId = conversationId,
            Sender         = sender,
            MessageText    = messageText,
            Timestamp      = DateTime.UtcNow
        };
        return await _messages.CreateAsync(msg);
    }

    public Task<IEnumerable<Message>> GetConversationHistoryAsync(Guid conversationId) =>
        _messages.GetByConversationIdAsync(conversationId);

    public Task<IReadOnlyList<Message>> GetRecentConversationHistoryAsync(
        Guid conversationId, int limit, CancellationToken ct = default) =>
        _messages.GetRecentByConversationIdAsync(conversationId, limit, ct);
}
