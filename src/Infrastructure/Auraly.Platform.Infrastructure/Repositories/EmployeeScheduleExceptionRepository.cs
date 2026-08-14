using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class EmployeeScheduleExceptionRepository : IEmployeeScheduleExceptionRepository
{
    private readonly ApplicationDbContext _context;

    public EmployeeScheduleExceptionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<EmployeeScheduleException?> GetByIdAsync(Guid employeeScheduleExceptionId, CancellationToken ct = default)
    {
        return _context.EmployeeScheduleExceptions
            .FirstOrDefaultAsync(e => e.EmployeeScheduleExceptionId == employeeScheduleExceptionId, ct);
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

    public Task<EmployeeScheduleException> AddAsync(EmployeeScheduleException exception, CancellationToken ct = default)
    {
        _context.EmployeeScheduleExceptions.Add(exception);
        return Task.FromResult(exception);
    }

    public Task<EmployeeScheduleException> UpdateAsync(EmployeeScheduleException exception, CancellationToken ct = default)
    {
        _context.EmployeeScheduleExceptions.Update(exception);
        return Task.FromResult(exception);
    }

    public Task DeleteAsync(EmployeeScheduleException exception, CancellationToken ct = default)
    {
        _context.EmployeeScheduleExceptions.Remove(exception);
        return Task.CompletedTask;
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
