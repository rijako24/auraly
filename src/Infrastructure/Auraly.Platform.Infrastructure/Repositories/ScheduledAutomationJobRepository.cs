using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class ScheduledAutomationJobRepository : IScheduledAutomationJobRepository
{
    private readonly ApplicationDbContext _context;

    public ScheduledAutomationJobRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<string, ScheduledAutomationJob>> GetByDeduplicationKeysAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return new Dictionary<string, ScheduledAutomationJob>(StringComparer.OrdinalIgnoreCase);

        return await _context.ScheduledAutomationJobs
            .Where(j => keys.Contains(j.DeduplicationKey))
            .ToDictionaryAsync(j => j.DeduplicationKey, StringComparer.OrdinalIgnoreCase, ct);
    }

    public async Task<IReadOnlyList<ScheduledAutomationJob>> GetDueAsync(
        DateTime utcNow,
        int limit,
        CancellationToken ct = default)
    {
        return await _context.ScheduledAutomationJobs
            .Include(j => j.Reservation)
                .ThenInclude(r => r.Service)
            .Include(j => j.Reservation)
                .ThenInclude(r => r.AddOns)
                    .ThenInclude(a => a.AddOnService)
            .Where(j =>
                j.ScheduledAtUtc <= utcNow &&
                (j.Status == ScheduledAutomationJobStatus.Pending ||
                 (j.Status == ScheduledAutomationJobStatus.Locked &&
                  j.LockedUntilUtc.HasValue &&
                  j.LockedUntilUtc <= utcNow)))
            .OrderBy(j => j.ScheduledAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<ScheduledAutomationJob?> GetByIdAsync(Guid jobId, CancellationToken ct = default) =>
        _context.ScheduledAutomationJobs
            .Include(j => j.Reservation)
                .ThenInclude(r => r.Service)
            .FirstOrDefaultAsync(j => j.ScheduledAutomationJobId == jobId, ct);

    public Task<ScheduledAutomationJob?> GetLatestByReservationAndTypeAsync(
        Guid businessId,
        Guid reservationId,
        ScheduledAutomationJobType jobType,
        CancellationToken ct = default)
    {
        return _context.ScheduledAutomationJobs
            .Where(j => j.BusinessId == businessId &&
                        j.ReservationId == reservationId &&
                        j.JobType == jobType)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ScheduledAutomationJob> AddAsync(ScheduledAutomationJob job, CancellationToken ct = default)
    {
        _context.ScheduledAutomationJobs.Add(job);
        return Task.FromResult(job);
    }

    public Task<ScheduledAutomationJob> UpdateAsync(ScheduledAutomationJob job, CancellationToken ct = default)
    {
        _context.ScheduledAutomationJobs.Update(job);
        return Task.FromResult(job);
    }
}
