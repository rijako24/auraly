using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Gating;

public interface IConversationVerificationService
{
    Task RecordAsync(
        Guid conversationId,
        Guid businessId,
        string factType,
        string scopeKey,
        TimeSpan? ttl,
        string? payloadJson = null,
        CancellationToken ct = default);

    Task<bool> IsActiveAsync(
        Guid conversationId,
        string factType,
        string scopeKey,
        CancellationToken ct = default);
}

public sealed class ConversationVerificationService : IConversationVerificationService
{
    private readonly IUnitOfWork _unitOfWork;

    public ConversationVerificationService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task RecordAsync(
        Guid conversationId,
        Guid businessId,
        string factType,
        string scopeKey,
        TimeSpan? ttl,
        string? payloadJson = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _unitOfWork.ConversationVerifications.UpsertAsync(new Domain.Entities.ConversationVerification
        {
            VerificationId = Guid.NewGuid(),
            ConversationId = conversationId,
            BusinessId = businessId,
            FactType = factType,
            ScopeKey = scopeKey,
            PayloadJson = payloadJson,
            VerifiedAt = now,
            ExpiresAt = ttl.HasValue ? now.Add(ttl.Value) : null
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task<bool> IsActiveAsync(
        Guid conversationId,
        string factType,
        string scopeKey,
        CancellationToken ct = default) =>
        _unitOfWork.ConversationVerifications.ExistsActiveAsync(
            conversationId, factType, scopeKey, DateTime.UtcNow, ct);
}
