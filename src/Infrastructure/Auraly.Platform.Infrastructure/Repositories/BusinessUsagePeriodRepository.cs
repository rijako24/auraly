using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class BusinessUsagePeriodRepository : IBusinessUsagePeriodRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessUsagePeriodRepository(ApplicationDbContext context) => _context = context;

    public async Task<BusinessUsagePeriod?> GetCurrentAsync(Guid businessSubscriptionId, DateTime utcNow, CancellationToken ct = default) =>
        await _context.BusinessUsagePeriods
            .Include(p => p.BusinessSubscription)
            .FirstOrDefaultAsync(p =>
                p.BusinessSubscriptionId == businessSubscriptionId
                && p.PeriodStart <= utcNow
                && p.PeriodEnd > utcNow, ct);

    public async Task<BusinessUsagePeriod?> GetCurrentByBusinessIdAsync(Guid businessId, DateTime utcNow, CancellationToken ct = default) =>
        await _context.BusinessUsagePeriods
            .Include(p => p.BusinessSubscription)
            .FirstOrDefaultAsync(p =>
                p.BusinessId == businessId
                && p.PeriodStart <= utcNow
                && p.PeriodEnd > utcNow, ct);

    public async Task<BusinessUsagePeriod> AddAsync(BusinessUsagePeriod period, CancellationToken ct = default)
    {
        _context.BusinessUsagePeriods.Add(period);
        try
        {
            await _context.SaveChangesAsync(ct);
            return period;
        }
        catch (DbUpdateException)
        {
            _context.Entry(period).State = EntityState.Detached;
            var concurrentlyCreated = await _context.BusinessUsagePeriods
                .Include(value => value.BusinessSubscription)
                .FirstOrDefaultAsync(value =>
                    value.BusinessSubscriptionId == period.BusinessSubscriptionId
                    && value.PeriodStart == period.PeriodStart
                    && value.PeriodEnd == period.PeriodEnd,
                    ct);
            if (concurrentlyCreated is not null)
                return concurrentlyCreated;
            throw;
    }
    }

    public async Task UpdateAsync(BusinessUsagePeriod period, CancellationToken ct = default)
    {
        period.UpdatedAt = DateTime.UtcNow;
        _context.BusinessUsagePeriods.Update(period);
        await _context.SaveChangesAsync(ct);
    }
}
