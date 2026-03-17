using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IEmployeeAdminService
{
    Task<EmployeeDto> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<PagedResponse<EmployeeDto>> GetPagedByBusinessIdAsync(Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct = default);
    Task<EmployeeDto> CreateAsync(Guid tenantId, CreateEmployeeRequest request, CancellationToken ct = default);
    Task<EmployeeDto> UpdateAsync(Guid tenantId, Guid employeeId, UpdateEmployeeRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
