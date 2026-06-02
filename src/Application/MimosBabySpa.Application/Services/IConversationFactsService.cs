namespace MimosBabySpa.Application.Services;

public interface IConversationFactsService
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid conversationId, CancellationToken ct = default);
    Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default);
    Task SetAsync(
        Guid conversationId,
        Guid businessId,
        string key,
        string value,
        bool persistsAcrossConversations = false,
        CancellationToken ct = default);
}
