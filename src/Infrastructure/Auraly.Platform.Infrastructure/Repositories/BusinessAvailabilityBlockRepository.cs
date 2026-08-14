using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class BusinessAvailabilityBlockRepository : IBusinessAvailabilityBlockRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessAvailabilityBlockRepository(ApplicationDbContext context) => _context = context;

    public Task<BusinessAvailabilityBlock?> GetByIdAsync(Guid businessAvailabilityBlockId, CancellationToken ct = default) =>
        _context.BusinessAvailabilityBlocks.FirstOrDefaultAsync(b => b.BusinessAvailabilityBlockId == businessAvailabilityBlockId, ct);

    public async Task<IReadOnlyList<BusinessAvailabilityBlock>> GetByBusinessAndDateAsync(Guid businessId, DateOnly date, CancellationToken ct = default) =>
        await _context.BusinessAvailabilityBlocks
            .Where(b => b.BusinessId == businessId && b.Date == date && b.IsActive)
            .OrderBy(b => b.StartTime)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BusinessAvailabilityBlock>> GetByBusinessAndDateRangeAsync(Guid businessId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default) =>
        await _context.BusinessAvailabilityBlocks
            .Where(b => b.BusinessId == businessId && b.Date >= startDate && b.Date <= endDate && b.IsActive)
            .OrderBy(b => b.Date)
            .ThenBy(b => b.StartTime)
            .ToListAsync(ct);

    public Task<BusinessAvailabilityBlock> AddAsync(BusinessAvailabilityBlock block, CancellationToken ct = default)
    {
        _context.BusinessAvailabilityBlocks.Add(block);
        return Task.FromResult(block);
    }

    public Task<BusinessAvailabilityBlock> UpdateAsync(BusinessAvailabilityBlock block, CancellationToken ct = default)
    {
        _context.BusinessAvailabilityBlocks.Update(block);
        return Task.FromResult(block);
    }
}
