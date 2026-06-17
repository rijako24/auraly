using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Testing;

internal sealed class AgentTestConversationFactsService : IConversationFactsService
{
    private readonly IConversationFactsService _inner;
    private readonly IDictionary<string, string> _facts;

    public AgentTestConversationFactsService(
        IConversationFactsService inner,
        IDictionary<string, string> facts)
    {
        _inner = inner;
        _facts = facts;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var dbFacts = await _inner.GetAllAsync(conversationId, ct);
        var merged = new Dictionary<string, string>(dbFacts, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in _facts)
        {
            if (!string.IsNullOrWhiteSpace(value))
                merged[key] = value;
        }

        return merged;
    }

    public async Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var dbRecords = await _inner.GetAllRecordsAsync(conversationId, ct);
        var records = dbRecords
            .Where(r => !_facts.ContainsKey(r.Key))
            .ToList();

        var now = DateTime.UtcNow;
        records.AddRange(_facts
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => new ConversationFactRecord(kv.Key, kv.Value, now, now)));

        return records;
    }

    public async Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default)
    {
        if (_facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        return await _inner.GetAsync(conversationId, key, ct);
    }

    public Task SetAsync(
        Guid conversationId,
        Guid businessId,
        string key,
        string value,
        bool rememberAcrossRequests = false,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _facts[key] = value;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ClearNonPersistentAsync(
        Guid conversationId,
        IReadOnlyCollection<string> persistentKeys,
        CancellationToken ct = default)
    {
        var keep = persistentKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cleared = _facts.Keys
            .Where(key => !keep.Contains(key))
            .ToList();

        foreach (var key in cleared)
            _facts.Remove(key);

        return Task.FromResult<IReadOnlyList<string>>(cleared);
    }

    public Task<IReadOnlyList<string>> ClearFieldsAsync(
        Guid conversationId,
        IReadOnlyCollection<string> fields,
        CancellationToken ct = default)
    {
        var delete = fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cleared = _facts.Keys
            .Where(delete.Contains)
            .ToList();

        foreach (var key in cleared)
            _facts.Remove(key);

        return Task.FromResult<IReadOnlyList<string>>(cleared);
    }
}
