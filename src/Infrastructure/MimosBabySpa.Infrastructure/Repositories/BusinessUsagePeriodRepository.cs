using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
        await _context.SaveChangesAsync(ct);
        return period;
    }

    public async Task UpdateAsync(BusinessUsagePeriod period, CancellationToken ct = default)
    {
        period.UpdatedAt = DateTime.UtcNow;
        _context.BusinessUsagePeriods.Update(period);
        await _context.SaveChangesAsync(ct);
    }
}
