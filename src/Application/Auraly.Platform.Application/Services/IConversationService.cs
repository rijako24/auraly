using Auraly.Platform.Application.DTOs;

namespace Auraly.Platform.Application.Services;

public interface IConversationService
{
    Task<Domain.Entities.Conversation> GetOrCreateConversationAsync(Guid businessId, string userNumber, string? customerName = null);
    Task UpdateConversationContextAsync(Guid conversationId, string? lastMessage);
    Task UpdateConversationAsync(Domain.Entities.Conversation conversation, CancellationToken ct = default);
    Task<Domain.Entities.Conversation?> GetConversationByIdAsync(Guid conversationId);
    Task<bool> HasClosedConversationsAsync(Guid businessId, string userNumber, CancellationToken ct = default);
}
