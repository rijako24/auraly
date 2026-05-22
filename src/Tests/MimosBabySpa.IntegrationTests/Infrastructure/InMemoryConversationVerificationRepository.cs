using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public sealed class InMemoryConversationVerificationRepository : IConversationVerificationRepository
{
    private readonly List<ConversationVerification> _items = [];

    public Task<bool> ExistsActiveAsync(
        Guid conversationId,
        string factType,
        string scopeKey,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        var exists = _items.Any(v =>
            v.ConversationId == conversationId
            && v.FactType.Equals(factType, StringComparison.OrdinalIgnoreCase)
            && v.ScopeKey.Equals(scopeKey, StringComparison.OrdinalIgnoreCase)
            && (v.ExpiresAt == null || v.ExpiresAt > utcNow));

        return Task.FromResult(exists);
    }

    public Task UpsertAsync(ConversationVerification verification, CancellationToken ct = default)
    {
        _items.RemoveAll(v =>
            v.ConversationId == verification.ConversationId
            && v.FactType.Equals(verification.FactType, StringComparison.OrdinalIgnoreCase)
            && v.ScopeKey.Equals(verification.ScopeKey, StringComparison.OrdinalIgnoreCase));

        _items.Add(verification);
        return Task.CompletedTask;
    }
}
