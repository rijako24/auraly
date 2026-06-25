using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ExternalEscalationAttemptRepository : IExternalEscalationAttemptRepository
{
    private readonly ApplicationDbContext _context;

    public ExternalEscalationAttemptRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ExternalEscalationAttempt?> GetByIdAsync(Guid attemptId, CancellationToken ct = default) =>
        _context.ExternalEscalationAttempts.FirstOrDefaultAsync(o => o.ExternalEscalationAttemptId == attemptId, ct);

    public Task<ExternalEscalationAttempt?> GetByAttemptCodeAsync(
        Guid businessId,
        string attemptCode,
        string phone,
        CancellationToken ct = default)
    {
        var normalized = NormalizePhone(phone);
        return _context.ExternalEscalationAttempts
            .Where(o => o.BusinessId == businessId
                && o.AttemptCode == attemptCode
                && o.ContactPhoneSnapshot == normalized
                && o.Status == ExternalEscalationAttemptStatus.Pending)
            .OrderByDescending(o => o.EscalatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ExternalEscalationAttempt?> GetByWhatsAppMessageIdAsync(
        Guid businessId,
        string whatsAppMessageId,
        string phone,
        CancellationToken ct = default)
    {
        var normalized = NormalizePhone(phone);
        return _context.ExternalEscalationAttempts
            .Where(o => o.BusinessId == businessId
                && o.WhatsAppMessageId == whatsAppMessageId
                && o.ContactPhoneSnapshot == normalized
                && o.Status == ExternalEscalationAttemptStatus.Pending)
            .OrderByDescending(o => o.EscalatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ExternalEscalationAttempt?> GetLatestByAttemptCodeForContactAsync(
        Guid businessId,
        string attemptCode,
        string phone,
        CancellationToken ct = default)
    {
        var normalized = NormalizePhone(phone);
        return _context.ExternalEscalationAttempts
            .Where(o => o.BusinessId == businessId
                && o.AttemptCode == attemptCode
                && o.ContactPhoneSnapshot == normalized)
            .OrderByDescending(o => o.EscalatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ExternalEscalationAttempt>> GetRecentByContactPhoneAsync(
        Guid businessId,
        string phone,
        int limit,
        bool includeCompleted = false,
        CancellationToken ct = default)
    {
        var normalized = NormalizePhone(phone);
        var query = _context.ExternalEscalationAttempts
            .Where(o => o.BusinessId == businessId && o.ContactPhoneSnapshot == normalized);

        if (!includeCompleted)
            query = query.Where(o => o.Status == ExternalEscalationAttemptStatus.Pending);

        return await query
            .OrderByDescending(o => o.EscalatedAt)
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<ExternalEscalationAttempt>> GetPendingByContactPhoneAsync(
        Guid businessId,
        string phone,
        CancellationToken ct = default)
    {
        var normalized = NormalizePhone(phone);
        return await _context.ExternalEscalationAttempts
            .Where(o => o.BusinessId == businessId
                && o.ContactPhoneSnapshot == normalized
                && o.Status == ExternalEscalationAttemptStatus.Pending)
            .OrderByDescending(o => o.EscalatedAt)
            .ToListAsync(ct);
    }


    public async Task<IReadOnlyList<ExternalEscalationAttempt>> GetExpiredPendingAttemptsAsync(
        DateTime utcNow,
        CancellationToken ct = default)
    {
        return await _context.ExternalEscalationAttempts
            .Where(o => o.Status == ExternalEscalationAttemptStatus.Pending && o.ExpiresAt <= utcNow)
            .OrderBy(o => o.ExpiresAt)
            .ToListAsync(ct);
    }

    public Task<int> CountAttemptsAsync(
        Guid businessId,
        string eventName,
        string targetType,
        Guid targetId,
        CancellationToken ct = default) =>
        _context.ExternalEscalationAttempts.CountAsync(o =>
            o.BusinessId == businessId
            && o.EventName == eventName
            && o.TargetType == targetType
            && o.TargetId == targetId,
            ct);

    public async Task<IReadOnlyList<ExternalEscalationAttempt>> GetAttemptsForTargetAsync(
        Guid businessId,
        string eventName,
        string targetType,
        Guid targetId,
        CancellationToken ct = default)
    {
        return await _context.ExternalEscalationAttempts
            .Where(o => o.BusinessId == businessId
                && o.EventName == eventName
                && o.TargetType == targetType
                && o.TargetId == targetId)
            .OrderBy(o => o.EscalatedAt)
            .ToListAsync(ct);
    }
    public Task<bool> HasAcceptedForTargetAsync(
        Guid businessId,
        string eventName,
        string targetType,
        Guid targetId,
        CancellationToken ct = default) =>
        _context.ExternalEscalationAttempts.AnyAsync(o =>
            o.BusinessId == businessId
            && o.EventName == eventName
            && o.TargetType == targetType
            && o.TargetId == targetId
            && o.Status == ExternalEscalationAttemptStatus.Accepted,
            ct);

    public Task<ExternalEscalationAttempt> AddAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default)
    {
        attempt.ContactPhoneSnapshot = NormalizePhone(attempt.ContactPhoneSnapshot);
        _context.ExternalEscalationAttempts.Add(attempt);
        return Task.FromResult(attempt);
    }

    public Task<ExternalEscalationAttempt> UpdateAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default)
    {
        attempt.ContactPhoneSnapshot = NormalizePhone(attempt.ContactPhoneSnapshot);
        _context.ExternalEscalationAttempts.Update(attempt);
        return Task.FromResult(attempt);
    }

    public async Task CancelPendingForTargetAsync(
        Guid businessId,
        string eventName,
        string targetType,
        Guid targetId,
        Guid exceptAttemptId,
        CancellationToken ct = default)
    {
        var open = await _context.ExternalEscalationAttempts
            .Where(o => o.BusinessId == businessId
                && o.EventName == eventName
                && o.TargetType == targetType
                && o.TargetId == targetId
                && o.ExternalEscalationAttemptId != exceptAttemptId
                && o.Status == ExternalEscalationAttemptStatus.Pending)
            .ToListAsync(ct);

        foreach (var attempt in open)
        {
            attempt.Status = ExternalEscalationAttemptStatus.Cancelled;
            attempt.CancelledAt = DateTime.UtcNow;
        }
    }

    private static string NormalizePhone(string phone) =>
        new(phone.Where(char.IsDigit).ToArray());
}
