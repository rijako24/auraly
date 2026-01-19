using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IConversationRepository
{
    Task<Conversation?> GetByUserNumberAsync(string userNumber);
    Task<Conversation?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber);
    Task<Conversation> CreateAsync(Conversation conversation);
    Task<Conversation> UpdateAsync(Conversation conversation);
    Task<Conversation?> GetByIdAsync(Guid conversationId);
}
