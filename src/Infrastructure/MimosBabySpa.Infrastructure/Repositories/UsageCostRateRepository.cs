using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class UsageCostRateRepository : IUsageCostRateRepository
{
    private readonly ApplicationDbContext _context;

    public UsageCostRateRepository(ApplicationDbContext context) => _context = context;

    public async Task<UsageCostRate?> GetActiveAsync(string code, UsageOperationType operationType, DateTime utcNow, CancellationToken ct = default) =>
        await _context.UsageCostRates
            .AsNoTracking()
            .Where(r => r.Code == code
                        && r.OperationType == operationType
                        && r.IsActive
                        && r.EffectiveFrom <= utcNow
                        && (r.EffectiveTo == null || r.EffectiveTo > utcNow))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<UsageCostRate>> GetActiveAsync(CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        return await _context.UsageCostRates
            .AsNoTracking()
            .Where(r => r.IsActive
                        && r.EffectiveFrom <= utcNow
                        && (r.EffectiveTo == null || r.EffectiveTo > utcNow))
            .OrderBy(r => r.OperationType)
            .ThenBy(r => r.Code)
            .ToListAsync(ct);
    }
}
