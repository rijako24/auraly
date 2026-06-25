using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IExternalEscalationAttemptRepository
{
    Task<ExternalEscalationAttempt?> GetByIdAsync(Guid attemptId, CancellationToken ct = default);
    Task<ExternalEscalationAttempt?> GetByAttemptCodeAsync(Guid businessId, string attemptCode, string phone, CancellationToken ct = default);
    Task<ExternalEscalationAttempt?> GetLatestByAttemptCodeForContactAsync(Guid businessId, string attemptCode, string phone, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEscalationAttempt>> GetRecentByContactPhoneAsync(Guid businessId, string phone, int limit, bool includeCompleted = false, CancellationToken ct = default);
    Task<ExternalEscalationAttempt?> GetByWhatsAppMessageIdAsync(Guid businessId, string whatsAppMessageId, string phone, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEscalationAttempt>> GetPendingByContactPhoneAsync(Guid businessId, string phone, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEscalationAttempt>> GetExpiredPendingAttemptsAsync(DateTime utcNow, CancellationToken ct = default);
    Task<int> CountAttemptsAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEscalationAttempt>> GetAttemptsForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default);
    Task<bool> HasAcceptedForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default);
    Task<ExternalEscalationAttempt> AddAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default);
    Task<ExternalEscalationAttempt> UpdateAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default);
    Task CancelPendingForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, Guid exceptAttemptId, CancellationToken ct = default);
}
