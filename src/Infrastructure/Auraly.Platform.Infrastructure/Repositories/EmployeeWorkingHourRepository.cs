using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class EmployeeWorkingHourRepository : IEmployeeWorkingHourRepository
{
    private readonly ApplicationDbContext _context;

    public EmployeeWorkingHourRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdAsync(Guid businessId, Guid employeeId, CancellationToken ct = default)
    {
        return await _context.EmployeeWorkingHours
            .Where(h => h.BusinessId == businessId && h.EmployeeId == employeeId && h.IsActive)
            .OrderBy(h => h.DayOfWeek)
            .ThenBy(h => h.OpenTime)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdsAsync(Guid businessId, IEnumerable<Guid> employeeIds, CancellationToken ct = default)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await _context.EmployeeWorkingHours
            .Where(h => h.BusinessId == businessId && ids.Contains(h.EmployeeId) && h.IsActive)
            .OrderBy(h => h.EmployeeId)
            .ThenBy(h => h.DayOfWeek)
            .ThenBy(h => h.OpenTime)
            .ToListAsync(ct);
    }

    public async Task ReplaceForEmployeeAsync(Guid businessId, Guid employeeId, IEnumerable<EmployeeWorkingHour> workingHours, CancellationToken ct = default)
    {
        var current = await _context.EmployeeWorkingHours
            .Where(h => h.BusinessId == businessId && h.EmployeeId == employeeId)
            .ToListAsync(ct);

        _context.EmployeeWorkingHours.RemoveRange(current);
        await _context.EmployeeWorkingHours.AddRangeAsync(workingHours, ct);
    }
}
