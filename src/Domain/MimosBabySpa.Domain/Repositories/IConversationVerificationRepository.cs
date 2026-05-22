using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IConversationVerificationRepository
{
    Task<bool> ExistsActiveAsync(
        Guid conversationId,
        string factType,
        string scopeKey,
        DateTime utcNow,
        CancellationToken ct = default);

    Task UpsertAsync(ConversationVerification verification, CancellationToken ct = default);
}
