using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class BusinessWorkingHourRepository : IBusinessWorkingHourRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessWorkingHourRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<BusinessWorkingHour>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default)
    {
        return await _context.BusinessWorkingHours
            .Where(h => h.BusinessId == businessId && h.IsActive)
            .OrderBy(h => h.DayOfWeek)
            .ThenBy(h => h.OpenTime)
            .ToListAsync(ct);
    }

    public async Task ReplaceForBusinessAsync(Guid businessId, IEnumerable<BusinessWorkingHour> workingHours, CancellationToken ct = default)
    {
        var current = await _context.BusinessWorkingHours
            .Where(h => h.BusinessId == businessId)
            .ToListAsync(ct);

        _context.BusinessWorkingHours.RemoveRange(current);
        await _context.BusinessWorkingHours.AddRangeAsync(workingHours, ct);
    }
}
