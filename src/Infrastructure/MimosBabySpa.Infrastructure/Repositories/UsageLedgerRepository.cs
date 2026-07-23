using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class UsageLedgerRepository : IUsageLedgerRepository
{
    private readonly ApplicationDbContext _context;

    public UsageLedgerRepository(ApplicationDbContext context) => _context = context;

    public async Task<UsageLedgerEntry> AddAsync(UsageLedgerEntry entry, CancellationToken ct = default)
    {
        _context.UsageLedgerEntries.Add(entry);
        await _context.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<UsageLedgerEntry>> GetRecentByBusinessIdAsync(Guid businessId, int limit, CancellationToken ct = default) =>
        await _context.UsageLedgerEntries
            .AsNoTracking()
            .Where(e => e.BusinessId == businessId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UsageLedgerEntry>> GetByPeriodIdAsync(
        Guid businessUsagePeriodId,
        CancellationToken ct = default) =>
        await _context.UsageLedgerEntries
            .AsNoTracking()
            .Where(e => e.BusinessUsagePeriodId == businessUsagePeriodId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
}
