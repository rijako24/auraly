using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class EmployeeScheduleExceptionRepository : IEmployeeScheduleExceptionRepository
{
    private readonly ApplicationDbContext _context;

    public EmployeeScheduleExceptionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default)
    {
        return await _context.EmployeeScheduleExceptions
            .Where(e => e.EmployeeId == employeeId)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.OpenTime)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdsAndDateAsync(
        IEnumerable<Guid> employeeIds,
        DateOnly date,
        CancellationToken ct = default)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await _context.EmployeeScheduleExceptions
            .Where(e => ids.Contains(e.EmployeeId) && e.Date == date)
            .ToListAsync(ct);
    }

    public async Task ReplaceForEmployeeAsync(Guid employeeId, IEnumerable<EmployeeScheduleException> exceptions, CancellationToken ct = default)
    {
        var current = await _context.EmployeeScheduleExceptions
            .Where(e => e.EmployeeId == employeeId)
            .ToListAsync(ct);

        _context.EmployeeScheduleExceptions.RemoveRange(current);
        await _context.EmployeeScheduleExceptions.AddRangeAsync(exceptions, ct);
    }
}
