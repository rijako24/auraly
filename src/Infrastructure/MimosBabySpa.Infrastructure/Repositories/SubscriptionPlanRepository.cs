using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
