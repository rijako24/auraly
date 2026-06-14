using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class WorkingHoursAdminService : IWorkingHoursAdminService
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkingHoursAdminService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<WorkingHourDto>> GetBusinessWorkingHoursAsync(Guid tenantId, Guid businessId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var hours = await _unitOfWork.BusinessWorkingHours.GetByBusinessIdAsync(businessId, ct);
        return hours.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<WorkingHourDto>> UpdateBusinessWorkingHoursAsync(
        Guid tenantId,
        Guid businessId,
        UpdateWorkingHoursRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var hours = request.WorkingHours.Select(h => new BusinessWorkingHour
        {
            BusinessWorkingHourId = Guid.NewGuid(),
            BusinessId = businessId,
            DayOfWeek = ToDayOfWeek(h.DayOfWeek),
            OpenTime = ParseTime(h.OpenTime),
            CloseTime = ParseTime(h.CloseTime),
            IsActive = h.IsActive,
            CreatedAt = DateTime.UtcNow
        }).Where(h => h.OpenTime < h.CloseTime).ToList();

        await _unitOfWork.BusinessWorkingHours.ReplaceForBusinessAsync(businessId, hours, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetBusinessWorkingHoursAsync(tenantId, businessId, ct);
    }

    public async Task<EmployeeWorkingHoursDto> GetEmployeeWorkingHoursAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var employee = await GetEmployeeForTenantAsync(tenantId, employeeId, ct);
        var hours = await _unitOfWork.EmployeeWorkingHours.GetByEmployeeIdAsync(employeeId, ct);
        return new EmployeeWorkingHoursDto(
            employeeId,
            hours.Count == 0,
            hours.Select(Map).ToList());
    }

    public async Task<EmployeeWorkingHoursDto> UpdateEmployeeWorkingHoursAsync(
        Guid tenantId,
        Guid employeeId,
        UpdateWorkingHoursRequest request,
        CancellationToken ct = default)
    {
        var employee = await GetEmployeeForTenantAsync(tenantId, employeeId, ct);
        var hours = request.WorkingHours.Select(h => new EmployeeWorkingHour
        {
            EmployeeWorkingHourId = Guid.NewGuid(),
            BusinessId = employee.BusinessId,
            EmployeeId = employeeId,
            DayOfWeek = ToDayOfWeek(h.DayOfWeek),
            OpenTime = ParseTime(h.OpenTime),
            CloseTime = ParseTime(h.CloseTime),
            IsActive = h.IsActive,
            CreatedAt = DateTime.UtcNow
        }).Where(h => h.OpenTime < h.CloseTime).ToList();

        await _unitOfWork.EmployeeWorkingHours.ReplaceForEmployeeAsync(employeeId, hours, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetEmployeeWorkingHoursAsync(tenantId, employeeId, ct);
    }

    private async Task<Employee> GetEmployeeForTenantAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId)
            ?? throw new NotFoundException(nameof(Employee), employeeId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, employee.BusinessId, ct);
        return employee;
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static WorkingHourDto Map(BusinessWorkingHour h) => new(
        h.BusinessWorkingHourId,
        (int)h.DayOfWeek,
        h.OpenTime.ToString(@"hh\:mm"),
        h.CloseTime.ToString(@"hh\:mm"),
        h.IsActive);

    private static WorkingHourDto Map(EmployeeWorkingHour h) => new(
        h.EmployeeWorkingHourId,
        (int)h.DayOfWeek,
        h.OpenTime.ToString(@"hh\:mm"),
        h.CloseTime.ToString(@"hh\:mm"),
        h.IsActive);

    private static DayOfWeek ToDayOfWeek(int value)
    {
        if (value < 0 || value > 6)
            throw new DomainValidationException("dayOfWeek", "Debe estar entre 0 y 6.");
        return (DayOfWeek)value;
    }

    private static TimeSpan ParseTime(string value)
    {
        if (!TimeSpan.TryParse(value, out var parsed))
            throw new DomainValidationException("time", $"Hora invalida: {value}");
        return parsed;
    }
}
