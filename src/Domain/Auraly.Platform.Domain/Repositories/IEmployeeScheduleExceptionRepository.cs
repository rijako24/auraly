using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IEmployeeScheduleExceptionRepository
{
    Task<EmployeeScheduleException?> GetByIdAsync(Guid employeeScheduleExceptionId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdsAndDateAsync(
        IEnumerable<Guid> employeeIds,
        DateOnly date,
        CancellationToken ct = default);
    Task<EmployeeScheduleException> AddAsync(EmployeeScheduleException exception, CancellationToken ct = default);
    Task<EmployeeScheduleException> UpdateAsync(EmployeeScheduleException exception, CancellationToken ct = default);
    Task DeleteAsync(EmployeeScheduleException exception, CancellationToken ct = default);
    Task ReplaceForEmployeeAsync(Guid employeeId, IEnumerable<EmployeeScheduleException> exceptions, CancellationToken ct = default);
}
