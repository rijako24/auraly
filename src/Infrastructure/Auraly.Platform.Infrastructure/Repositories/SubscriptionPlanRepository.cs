using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class SubscriptionPlanRepository : ISubscriptionPlanRepository
{
    private readonly ApplicationDbContext _context;

    public SubscriptionPlanRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync(CancellationToken ct = default) =>
        await _context.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPriceCop)
            .ToListAsync(ct);

    public async Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        await _context.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code && p.IsActive, ct);
}
