using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IWorkingHoursAdminService
{
    Task<IReadOnlyList<WorkingHourDto>> GetBusinessWorkingHoursAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkingHourDto>> UpdateBusinessWorkingHoursAsync(
        Guid tenantId,
        Guid businessId,
        UpdateWorkingHoursRequest request,
        CancellationToken ct = default);
    Task<EmployeeWorkingHoursDto> GetEmployeeWorkingHoursAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<EmployeeWorkingHoursDto> UpdateEmployeeWorkingHoursAsync(
        Guid tenantId,
        Guid employeeId,
        UpdateWorkingHoursRequest request,
        CancellationToken ct = default);
}
