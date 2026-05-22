using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents.Gating;

public interface IConversationVerificationService
{
    void Record(
        AgentToolContext ctx,
        string factType,
        string scopeKey,
        TimeSpan? ttl,
        string? payloadJson = null);

    bool IsActive(ConversationState state, string factType, string scopeKey);
}

public sealed class ConversationVerificationService : IConversationVerificationService
{
    private const int MaxEntries = 50;

    public void Record(
        AgentToolContext ctx,
        string factType,
        string scopeKey,
        TimeSpan? ttl,
        string? payloadJson = null)
    {
        var now = DateTime.UtcNow;
        var key = BuildKey(factType, scopeKey);

        ctx.ConversationState.Verifications[key] = new VerificationEntry(
            now,
            ttl.HasValue ? now.Add(ttl.Value) : null,
            payloadJson);

        PurgeExpired(ctx.ConversationState.Verifications);
        EnforceMaxSize(ctx.ConversationState.Verifications);
    }

    public bool IsActive(ConversationState state, string factType, string scopeKey)
    {
        PurgeExpired(state.Verifications);

        var key = BuildKey(factType, scopeKey);
        if (!state.Verifications.TryGetValue(key, out var entry))
            return false;

        return !entry.ExpiresAt.HasValue || entry.ExpiresAt > DateTime.UtcNow;
    }

    private static string BuildKey(string factType, string scopeKey) =>
        $"{factType}|{scopeKey}";

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
