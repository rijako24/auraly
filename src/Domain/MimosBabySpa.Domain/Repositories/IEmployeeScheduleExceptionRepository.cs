using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IEmployeeScheduleExceptionRepository
{
    Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdsAndDateAsync(
        IEnumerable<Guid> employeeIds,
        DateOnly date,
        CancellationToken ct = default);
    Task ReplaceForEmployeeAsync(Guid employeeId, IEnumerable<EmployeeScheduleException> exceptions, CancellationToken ct = default);
}
