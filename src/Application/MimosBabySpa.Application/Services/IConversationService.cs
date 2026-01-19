using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

public interface IConversationService
{
    Task<Domain.Entities.Conversation> GetOrCreateConversationAsync(Guid businessId, string userNumber, string? customerName = null);
    Task UpdateConversationContextAsync(Guid conversationId, string? lastMessage, string? lastIntent);
    Task<Domain.Entities.Conversation?> GetConversationByIdAsync(Guid conversationId);
}
