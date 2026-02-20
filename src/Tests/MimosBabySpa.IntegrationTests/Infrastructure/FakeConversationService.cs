using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// FakeConversationService — creates and tracks in-memory conversations
/// so ConversationStateManager can call GetOrCreateConversationAsync.
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
            c.BusinessId == businessId && c.UserNumber == userNumber);

        if (existing != null)
            return Task.FromResult(existing);

        var conv = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            BusinessId     = businessId,
            UserNumber     = userNumber,
            CustomerName   = customerName,
            Timestamp      = DateTime.UtcNow
        };
        _conversations.Add(conv);
        return Task.FromResult(conv);
    }

    public Task UpdateConversationContextAsync(
        Guid conversationId, string? lastMessage, string? lastIntent) =>
        Task.CompletedTask;

    public Task<Conversation?> GetConversationByIdAsync(Guid conversationId) =>
        Task.FromResult(_conversations.FirstOrDefault(c => c.ConversationId == conversationId));
}
