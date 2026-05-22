using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ConversationVerificationRepository : IConversationVerificationRepository
{
    private readonly ApplicationDbContext _context;

    public ConversationVerificationRepository(ApplicationDbContext context) => _context = context;

    public async Task<bool> ExistsActiveAsync(
        Guid conversationId,
        string factType,
        string scopeKey,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        return await _context.ConversationVerifications
            .AnyAsync(v =>
                v.ConversationId == conversationId
                && v.FactType == factType
                && v.ScopeKey == scopeKey
                && (v.ExpiresAt == null || v.ExpiresAt > utcNow),
                ct);
    }

    public async Task UpsertAsync(ConversationVerification verification, CancellationToken ct = default)
    {
        var existing = await _context.ConversationVerifications
            .FirstOrDefaultAsync(v =>
                v.ConversationId == verification.ConversationId
                && v.FactType == verification.FactType
                && v.ScopeKey == verification.ScopeKey,
                ct);

        if (existing is not null)
        {
            existing.PayloadJson = verification.PayloadJson;
            existing.VerifiedAt = verification.VerifiedAt;
            existing.ExpiresAt = verification.ExpiresAt;
            return;
        }

        await _context.ConversationVerifications.AddAsync(verification, ct);
    }
}
