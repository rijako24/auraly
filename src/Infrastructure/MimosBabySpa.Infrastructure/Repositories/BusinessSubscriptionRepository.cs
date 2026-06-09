using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class BusinessSubscriptionRepository : IBusinessSubscriptionRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessSubscriptionRepository(ApplicationDbContext context) => _context = context;

    public async Task<BusinessSubscription?> GetActiveByBusinessIdAsync(Guid businessId, CancellationToken ct = default) =>
        await _context.BusinessSubscriptions
            .Include(s => s.SubscriptionPlan)
            .Where(s => s.BusinessId == businessId
                        && s.Status == SubscriptionStatus.Active
                        && s.CurrentPeriodEnd > DateTime.UtcNow)
            .OrderByDescending(s => s.CurrentPeriodStart)
            .FirstOrDefaultAsync(ct);

    public async Task<BusinessSubscription> AddAsync(BusinessSubscription subscription, CancellationToken ct = default)
    {
        _context.BusinessSubscriptions.Add(subscription);
        await _context.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task UpdateAsync(BusinessSubscription subscription, CancellationToken ct = default)
    {
        subscription.UpdatedAt = DateTime.UtcNow;
        _context.BusinessSubscriptions.Update(subscription);
        await _context.SaveChangesAsync(ct);
    }
}
