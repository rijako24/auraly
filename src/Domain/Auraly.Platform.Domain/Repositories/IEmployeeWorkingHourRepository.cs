using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IEmployeeWorkingHourRepository
{
    Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdAsync(Guid businessId, Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdsAsync(Guid businessId, IEnumerable<Guid> employeeIds, CancellationToken ct = default);
    Task ReplaceForEmployeeAsync(Guid businessId, Guid employeeId, IEnumerable<EmployeeWorkingHour> workingHours, CancellationToken ct = default);
}
