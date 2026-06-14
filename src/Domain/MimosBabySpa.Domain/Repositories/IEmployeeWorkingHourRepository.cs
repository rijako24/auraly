using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IEmployeeWorkingHourRepository
{
    Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdsAsync(IEnumerable<Guid> employeeIds, CancellationToken ct = default);
    Task ReplaceForEmployeeAsync(Guid employeeId, IEnumerable<EmployeeWorkingHour> workingHours, CancellationToken ct = default);
}
