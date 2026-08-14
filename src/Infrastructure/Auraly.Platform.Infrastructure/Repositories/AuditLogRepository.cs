using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly ApplicationDbContext _context;

    public AuditLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AuditLog log, CancellationToken ct = default)
    {
        await _context.AuditLogs.AddAsync(log, ct);
    }

    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedAsync(
        Guid? tenantId, DateTime? from, DateTime? to, string? entityType,
        string? correlationId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.AuditLogs
            .Include(a => a.User)
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(a => a.TenantId == tenantId);
        if (from.HasValue)
            query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.Timestamp <= to.Value);
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(a => a.CorrelationId == correlationId);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
