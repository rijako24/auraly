using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IEmployeeServiceRepository
{
    Task<EmployeeService?> GetByIdAsync(Guid employeeServiceId);
    Task<IEnumerable<EmployeeService>> GetByEmployeeIdAsync(Guid employeeId);
    Task<IEnumerable<EmployeeService>> GetByServiceIdAsync(Guid serviceId);
    Task<bool> ExistsAsync(Guid employeeId, Guid serviceId);
    Task<EmployeeService> CreateAsync(EmployeeService employeeService);
    Task DeleteAsync(Guid employeeServiceId);
    Task<int> GetServiceCountByEmployeeIdAsync(Guid employeeId);
}
