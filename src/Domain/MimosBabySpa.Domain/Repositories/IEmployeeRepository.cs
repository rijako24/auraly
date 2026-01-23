using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid employeeId);
    Task<IEnumerable<Employee>> GetByBusinessIdAsync(Guid businessId);
    Task<IEnumerable<Employee>> GetActiveByBusinessIdAsync(Guid businessId);
    Task<IEnumerable<Employee>> GetByBusinessIdAndServiceIdAsync(Guid businessId, Guid serviceId);
    Task<Employee> CreateAsync(Employee employee);
    Task<Employee> UpdateAsync(Employee employee);
}
