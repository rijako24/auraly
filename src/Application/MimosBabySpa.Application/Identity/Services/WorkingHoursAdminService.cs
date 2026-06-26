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

    public async Task<IReadOnlyList<WorkingHourDto>> UpdateBusinessWorkingHoursAsync(Guid tenantId, Guid businessId, UpdateWorkingHoursRequest request, CancellationToken ct = default)
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
        return new EmployeeWorkingHoursDto(employeeId, hours.Count == 0, hours.Select(Map).ToList());
    }

    public async Task<EmployeeWorkingHoursDto> UpdateEmployeeWorkingHoursAsync(Guid tenantId, Guid employeeId, UpdateWorkingHoursRequest request, CancellationToken ct = default)
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

    public async Task<IReadOnlyList<BusinessAvailabilityBlockDto>> GetBusinessAvailabilityBlocksAsync(Guid tenantId, Guid businessId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var from = startDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var to = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90));
        if (from > to)
            throw new DomainValidationException("dateRange", "La fecha inicial debe ser anterior a la fecha final.");

        var blocks = await _unitOfWork.BusinessAvailabilityBlocks.GetByBusinessAndDateRangeAsync(businessId, from, to, ct);
        var employeeNames = await GetEmployeeNamesByBusinessAsync(businessId);
        return blocks.Select(block => Map(block, employeeNames)).ToList();
    }

    public async Task<BusinessAvailabilityBlockDto> CreateBusinessAvailabilityBlockAsync(Guid tenantId, Guid businessId, UpsertBusinessAvailabilityBlockRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        await EnsureEmployeeBelongsToBusinessAsync(request.EmployeeId, businessId, ct);
        var (startTime, endTime) = ParseOptionalTimeRange(request.StartTime, request.EndTime, requireBoth: false);
        var block = new BusinessAvailabilityBlock
        {
            BusinessAvailabilityBlockId = Guid.NewGuid(),
            BusinessId = businessId,
            EmployeeId = request.EmployeeId,
            Date = ParseDate(request.Date),
            StartTime = startTime,
            EndTime = endTime,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Bloqueo manual" : request.Reason.Trim(),
            Source = "admin",
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.BusinessAvailabilityBlocks.AddAsync(block, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        var employeeNames = await GetEmployeeNamesByBusinessAsync(businessId);
        return Map(block, employeeNames);
    }

    public async Task<BusinessAvailabilityBlockDto> UpdateBusinessAvailabilityBlockAsync(Guid tenantId, Guid businessId, Guid blockId, UpsertBusinessAvailabilityBlockRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var block = await _unitOfWork.BusinessAvailabilityBlocks.GetByIdAsync(blockId, ct)
            ?? throw new NotFoundException(nameof(BusinessAvailabilityBlock), blockId);
        if (block.BusinessId != businessId)
            throw new NotFoundException(nameof(BusinessAvailabilityBlock), blockId);

        await EnsureEmployeeBelongsToBusinessAsync(request.EmployeeId, businessId, ct);
        var (startTime, endTime) = ParseOptionalTimeRange(request.StartTime, request.EndTime, requireBoth: false);
        block.EmployeeId = request.EmployeeId;
        block.Date = ParseDate(request.Date);
        block.StartTime = startTime;
        block.EndTime = endTime;
        block.Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Bloqueo manual" : request.Reason.Trim();
        block.IsActive = request.IsActive;
        block.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.BusinessAvailabilityBlocks.UpdateAsync(block, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        var employeeNames = await GetEmployeeNamesByBusinessAsync(businessId);
        return Map(block, employeeNames);
    }

    public async Task DeleteBusinessAvailabilityBlockAsync(Guid tenantId, Guid businessId, Guid blockId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var block = await _unitOfWork.BusinessAvailabilityBlocks.GetByIdAsync(blockId, ct)
            ?? throw new NotFoundException(nameof(BusinessAvailabilityBlock), blockId);
        if (block.BusinessId != businessId)
            throw new NotFoundException(nameof(BusinessAvailabilityBlock), blockId);

        block.IsActive = false;
        block.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.BusinessAvailabilityBlocks.UpdateAsync(block, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeScheduleExceptionDto>> GetEmployeeScheduleExceptionsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        await GetEmployeeForTenantAsync(tenantId, employeeId, ct);
        var exceptions = await _unitOfWork.EmployeeScheduleExceptions.GetByEmployeeIdAsync(employeeId, ct);
        return exceptions.Select(Map).ToList();
    }

    public async Task<EmployeeScheduleExceptionDto> CreateEmployeeScheduleExceptionAsync(Guid tenantId, Guid employeeId, UpsertEmployeeScheduleExceptionRequest request, CancellationToken ct = default)
    {
        var employee = await GetEmployeeForTenantAsync(tenantId, employeeId, ct);
        var (openTime, closeTime) = ParseExceptionTimes(request);
        var exception = new EmployeeScheduleException
        {
            EmployeeScheduleExceptionId = Guid.NewGuid(),
            BusinessId = employee.BusinessId,
            EmployeeId = employeeId,
            Date = ParseDate(request.Date),
            OpenTime = openTime,
            CloseTime = closeTime,
            IsClosed = request.IsClosed,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.EmployeeScheduleExceptions.AddAsync(exception, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Map(exception);
    }

    public async Task<EmployeeScheduleExceptionDto> UpdateEmployeeScheduleExceptionAsync(Guid tenantId, Guid employeeId, Guid exceptionId, UpsertEmployeeScheduleExceptionRequest request, CancellationToken ct = default)
    {
        var employee = await GetEmployeeForTenantAsync(tenantId, employeeId, ct);
        var exception = await _unitOfWork.EmployeeScheduleExceptions.GetByIdAsync(exceptionId, ct)
            ?? throw new NotFoundException(nameof(EmployeeScheduleException), exceptionId);
        if (exception.EmployeeId != employeeId || exception.BusinessId != employee.BusinessId)
            throw new NotFoundException(nameof(EmployeeScheduleException), exceptionId);

        var (openTime, closeTime) = ParseExceptionTimes(request);
        exception.Date = ParseDate(request.Date);
        exception.OpenTime = openTime;
        exception.CloseTime = closeTime;
        exception.IsClosed = request.IsClosed;
        exception.Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        exception.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.EmployeeScheduleExceptions.UpdateAsync(exception, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Map(exception);
    }

    public async Task DeleteEmployeeScheduleExceptionAsync(Guid tenantId, Guid employeeId, Guid exceptionId, CancellationToken ct = default)
    {
        var employee = await GetEmployeeForTenantAsync(tenantId, employeeId, ct);
        var exception = await _unitOfWork.EmployeeScheduleExceptions.GetByIdAsync(exceptionId, ct)
            ?? throw new NotFoundException(nameof(EmployeeScheduleException), exceptionId);
        if (exception.EmployeeId != employeeId || exception.BusinessId != employee.BusinessId)
            throw new NotFoundException(nameof(EmployeeScheduleException), exceptionId);

        await _unitOfWork.EmployeeScheduleExceptions.DeleteAsync(exception, ct);
        await _unitOfWork.SaveChangesAsync(ct);
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

    private async Task EnsureEmployeeBelongsToBusinessAsync(Guid? employeeId, Guid businessId, CancellationToken ct)
    {
        if (!employeeId.HasValue) return;
        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId.Value)
            ?? throw new NotFoundException(nameof(Employee), employeeId.Value);
        if (employee.BusinessId != businessId)
            throw new NotFoundException(nameof(Employee), employeeId.Value);
    }

    private async Task<Dictionary<Guid, string>> GetEmployeeNamesByBusinessAsync(Guid businessId)
    {
        var employees = await _unitOfWork.Employees.GetByBusinessIdAsync(businessId);
        return employees.ToDictionary(employee => employee.EmployeeId, employee => employee.Name);
    }

    private static WorkingHourDto Map(BusinessWorkingHour h) => new(h.BusinessWorkingHourId, (int)h.DayOfWeek, h.OpenTime.ToString(@"hh\:mm"), h.CloseTime.ToString(@"hh\:mm"), h.IsActive);

    private static WorkingHourDto Map(EmployeeWorkingHour h) => new(h.EmployeeWorkingHourId, (int)h.DayOfWeek, h.OpenTime.ToString(@"hh\:mm"), h.CloseTime.ToString(@"hh\:mm"), h.IsActive);

    private static BusinessAvailabilityBlockDto Map(BusinessAvailabilityBlock block, IReadOnlyDictionary<Guid, string> employeeNames) => new(
        block.BusinessAvailabilityBlockId,
        block.BusinessId,
        block.EmployeeId,
        block.EmployeeId.HasValue && employeeNames.TryGetValue(block.EmployeeId.Value, out var name) ? name : null,
        block.Date.ToString("yyyy-MM-dd"),
        FormatTime(block.StartTime),
        FormatTime(block.EndTime),
        block.Reason,
        block.Source,
        block.IsActive,
        block.CreatedAt,
        block.UpdatedAt);

    private static EmployeeScheduleExceptionDto Map(EmployeeScheduleException exception) => new(
        exception.EmployeeScheduleExceptionId,
        exception.BusinessId,
        exception.EmployeeId,
        exception.Date.ToString("yyyy-MM-dd"),
        FormatTime(exception.OpenTime),
        FormatTime(exception.CloseTime),
        exception.IsClosed,
        exception.Reason,
        exception.CreatedAt,
        exception.UpdatedAt);

    private static DayOfWeek ToDayOfWeek(int value)
    {
        if (value < 0 || value > 6)
            throw new DomainValidationException("dayOfWeek", "Debe estar entre 0 y 6.");
        return (DayOfWeek)value;
    }

    private static DateOnly ParseDate(string value)
    {
        if (!DateOnly.TryParse(value, out var parsed))
            throw new DomainValidationException("date", $"Fecha invalida: {value}");
        return parsed;
    }

    private static TimeSpan ParseTime(string value)
    {
        if (!TimeSpan.TryParse(value, out var parsed))
            throw new DomainValidationException("time", $"Hora invalida: {value}");
        return parsed;
    }

    private static TimeSpan? ParseNullableTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return ParseTime(value);
    }

    private static (TimeSpan? Start, TimeSpan? End) ParseOptionalTimeRange(string? start, string? end, bool requireBoth)
    {
        var startTime = ParseNullableTime(start);
        var endTime = ParseNullableTime(end);
        if ((startTime.HasValue || endTime.HasValue || requireBoth) && (!startTime.HasValue || !endTime.HasValue))
            throw new DomainValidationException("timeRange", "Debe indicar hora inicial y final.");
        if (startTime.HasValue && endTime.HasValue && startTime >= endTime)
            throw new DomainValidationException("timeRange", "La hora inicial debe ser anterior a la final.");
        return (startTime, endTime);
    }

    private static (TimeSpan? Open, TimeSpan? Close) ParseExceptionTimes(UpsertEmployeeScheduleExceptionRequest request)
    {
        if (request.IsClosed)
            return (null, null);
        var (open, close) = ParseOptionalTimeRange(request.OpenTime, request.CloseTime, requireBoth: true);
        return (open, close);
    }

    private static string? FormatTime(TimeSpan? value) => value?.ToString(@"hh\:mm");
}
