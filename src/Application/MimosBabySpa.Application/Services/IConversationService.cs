using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

public interface IConversationService
{
    Task<Domain.Entities.Conversation> GetOrCreateConversationAsync(
        Guid businessId,
        string userNumber,
        string? customerName = null,
        Guid? agentId = null);
    Task<Domain.Entities.Conversation?> GetConversationByIdAsync(Guid conversationId);
}
