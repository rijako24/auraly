using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IMessageRepository
{
    Task<Message> CreateAsync(Message message);
    Task<IEnumerable<Message>> GetByConversationIdAsync(Guid conversationId);
    Task<Message?> GetByIdAsync(Guid messageId);
}
