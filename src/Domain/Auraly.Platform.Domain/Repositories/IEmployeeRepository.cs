using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid employeeId);
    Task<IEnumerable<Employee>> GetByBusinessIdAsync(Guid businessId);
    Task<IEnumerable<Employee>> GetActiveByBusinessIdAsync(Guid businessId);
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<IEnumerable<Employee>> GetByBusinessIdAndServiceIdAsync(Guid businessId, Guid serviceId);
    Task<Employee> CreateAsync(Employee employee);
    Task<Employee> UpdateAsync(Employee employee);
}
