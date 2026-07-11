using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents.Gating;

public interface IConversationVerificationService
{
    void Record(
        ConversationState state,
        string factType,
        IReadOnlyDictionary<string, string> dependencyFacts,
        TimeSpan? ttl);

    void Record(
        AgentToolContext ctx,
        string factType,
        IReadOnlyDictionary<string, string> dependencyFacts,
        TimeSpan? ttl);

    bool IsActive(
        ConversationState state,
        string factType,
        IReadOnlyDictionary<string, string> currentFacts);
}

public sealed class ConversationVerificationService : IConversationVerificationService
{
    private const int MaxEntries = 50;

    public void Record(
        AgentToolContext ctx,
        string factType,
        IReadOnlyDictionary<string, string> dependencyFacts,
        TimeSpan? ttl) =>
        Record(ctx.ConversationState, factType, dependencyFacts, ttl);

    public void Record(
        ConversationState state,
        string factType,
        IReadOnlyDictionary<string, string> dependencyFacts,
        TimeSpan? ttl)
    {
        var now = DateTime.UtcNow;
        var payloadJson = dependencyFacts.Count > 0
            ? VerificationSnapshot.Serialize(dependencyFacts)
            : null;

        state.Verifications[factType] = new VerificationEntry(
            now,
            ttl.HasValue ? now.Add(ttl.Value) : null,
            payloadJson);

        PurgeExpired(state.Verifications);
        EnforceMaxSize(state.Verifications);
    }

    public bool IsActive(
        ConversationState state,
        string factType,
        IReadOnlyDictionary<string, string> currentFacts)
    {
        PurgeExpired(state.Verifications);

        if (!state.Verifications.TryGetValue(factType, out var entry))
            return false;

        if (entry.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return false;

        return VerificationSnapshot.Matches(entry.PayloadJson, currentFacts);
    }

    private static void PurgeExpired(Dictionary<string, VerificationEntry> map)
    {
        var now = DateTime.UtcNow;
        var expired = map
            .Where(kv => kv.Value.ExpiresAt.HasValue && kv.Value.ExpiresAt <= now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expired)
            map.Remove(key);
    }

    private static void EnforceMaxSize(Dictionary<string, VerificationEntry> map)
    {
        if (map.Count <= MaxEntries)
            return;

        var toRemove = map
            .OrderBy(kv => kv.Value.VerifiedAt)
            .Take(map.Count - MaxEntries)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
            map.Remove(key);
    }
}
