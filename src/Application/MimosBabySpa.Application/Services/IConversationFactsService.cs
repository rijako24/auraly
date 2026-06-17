namespace MimosBabySpa.Application.Services;

public interface IConversationFactsService
{
    Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(Guid conversationId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid conversationId, CancellationToken ct = default);
    Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default);
    Task SetAsync(
        Guid conversationId,
        Guid businessId,
        string key,
        string value,
        bool rememberAcrossRequests = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ClearNonPersistentAsync(
        Guid conversationId,
        IReadOnlyCollection<string> persistentKeys,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ClearFieldsAsync(
        Guid conversationId,
        IReadOnlyCollection<string> fields,
        CancellationToken ct = default);
}

public sealed record ConversationFactRecord(
    string Key,
    string Value,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public DateTime LastTouchedAt => UpdatedAt ?? CreatedAt;
}
