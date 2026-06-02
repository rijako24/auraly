namespace MimosBabySpa.Application.Services;

public interface IMessageService
{
    Task<Domain.Entities.Message> SaveMessageAsync(
        Guid conversationId,
        string sender,
        string messageText);

    Task<IEnumerable<Domain.Entities.Message>> GetConversationHistoryAsync(Guid conversationId);

    Task<IReadOnlyList<Domain.Entities.Message>> GetRecentConversationHistoryAsync(
        Guid conversationId, int limit, CancellationToken ct = default);
}
