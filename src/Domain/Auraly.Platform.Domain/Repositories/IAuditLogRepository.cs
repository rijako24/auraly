using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedAsync(
        Guid? tenantId, DateTime? from, DateTime? to, string? entityType,
        string? correlationId, int page, int pageSize, CancellationToken ct = default);
}
