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

    Task<IReadOnlyList<BusinessAvailabilityBlockDto>> GetBusinessAvailabilityBlocksAsync(
        Guid tenantId,
        Guid businessId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken ct = default);
    Task<BusinessAvailabilityBlockDto> CreateBusinessAvailabilityBlockAsync(
        Guid tenantId,
        Guid businessId,
        UpsertBusinessAvailabilityBlockRequest request,
        CancellationToken ct = default);
    Task<BusinessAvailabilityBlockDto> UpdateBusinessAvailabilityBlockAsync(
        Guid tenantId,
        Guid businessId,
        Guid blockId,
        UpsertBusinessAvailabilityBlockRequest request,
        CancellationToken ct = default);
    Task DeleteBusinessAvailabilityBlockAsync(Guid tenantId, Guid businessId, Guid blockId, CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeScheduleExceptionDto>> GetEmployeeScheduleExceptionsAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default);
    Task<EmployeeScheduleExceptionDto> CreateEmployeeScheduleExceptionAsync(
        Guid tenantId,
        Guid employeeId,
        UpsertEmployeeScheduleExceptionRequest request,
        CancellationToken ct = default);
    Task<EmployeeScheduleExceptionDto> UpdateEmployeeScheduleExceptionAsync(
        Guid tenantId,
        Guid employeeId,
        Guid exceptionId,
        UpsertEmployeeScheduleExceptionRequest request,
        CancellationToken ct = default);
    Task DeleteEmployeeScheduleExceptionAsync(Guid tenantId, Guid employeeId, Guid exceptionId, CancellationToken ct = default);
}
