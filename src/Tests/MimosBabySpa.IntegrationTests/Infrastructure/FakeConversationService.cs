using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// FakeConversationService — creates and tracks in-memory conversations for integration tests.
/// </summary>
public class FakeConversationService : IConversationService
{
    private readonly List<Conversation> _conversations = [];
    private readonly Guid _businessId;

    public FakeConversationService(Guid businessId)
    {
        _businessId = businessId;
    }

    public Task<Conversation> GetOrCreateConversationAsync(
        Guid businessId, string userNumber, string? customerName = null)
    {
        var existing = _conversations.FirstOrDefault(c =>
            c.BusinessId == businessId
            && c.UserNumber == userNumber
            && c.Status == ConversationLifecycleStatus.Active);

        if (existing != null)
            return Task.FromResult(existing);

        var now = DateTime.UtcNow;
        var conv = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            CustomerName = customerName,
            Status = ConversationLifecycleStatus.Active,
            OpenedAt = now,
            LastActivityAt = now,
            Timestamp = now
        };
        _conversations.Add(conv);
        return Task.FromResult(conv);
    }

    public Task UpdateConversationContextAsync(
        Guid conversationId, string? lastMessage) =>
        Task.CompletedTask;

    public Task UpdateConversationAsync(Conversation conversation, CancellationToken ct = default)
    {
        var index = _conversations.FindIndex(c => c.ConversationId == conversation.ConversationId);
        if (index >= 0)
            _conversations[index] = conversation;
        else
            _conversations.Add(conversation);
        return Task.CompletedTask;
    }

    public Task<Conversation?> GetConversationByIdAsync(Guid conversationId) =>
        Task.FromResult(_conversations.FirstOrDefault(c => c.ConversationId == conversationId));

    public Task<bool> HasClosedConversationsAsync(
        Guid businessId, string userNumber, CancellationToken ct = default) =>
        Task.FromResult(_conversations.Any(c =>
            c.BusinessId == businessId
            && c.UserNumber == userNumber
            && c.Status == ConversationLifecycleStatus.Closed));
}
