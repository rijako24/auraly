using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class EmployeeAdminService : IEmployeeAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<EmployeeAdminService> _logger;

    public EmployeeAdminService(
        IUnitOfWork unitOfWork,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<EmployeeAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<EmployeeDto> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId)
            ?? throw new NotFoundException(nameof(Employee), employeeId);
        EnsureEmployeeBelongsToTenant(tenantId, employee);
        return MapToDto(employee);
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var employees = await _unitOfWork.Employees.GetByBusinessIdAsync(businessId);
        return employees.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<EmployeeDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var (items, totalCount) = await _unitOfWork.Employees.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, ct);
        return new PagedResponse<EmployeeDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<EmployeeDto> CreateAsync(Guid tenantId, CreateEmployeeRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, request.BusinessId, ct);

        var employee = new Employee
        {
            EmployeeId = Guid.NewGuid(),
            TenantId = tenantId,
            PartyId = request.PartyId,
            Name = request.Name,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Employees.CreateAsync(employee);

        var selectedServiceIds = (request.ServiceIds ?? []).Distinct().ToArray();
        if (selectedServiceIds.Length > 200)
            throw new ArgumentException("An employee cannot have more than 200 services.");
        var selectedServices = await _unitOfWork.Services.GetByIdsAsync(tenantId, selectedServiceIds, ct);
        foreach (var service in selectedServices)
        {
            if (service.BusinessId != request.BusinessId) continue;
            await _unitOfWork.EmployeeServices.CreateAsync(new EmployeeService
            {
                EmployeeServiceId = Guid.NewGuid(),
                EmployeeId = employee.EmployeeId,
                ServiceId = service.ServiceId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Employee '{Name}' created for business {BusinessId} [CorrelationId: {CorrelationId}]",
            employee.Name, request.BusinessId, _correlationIdProvider.CorrelationId);

        var created = await _unitOfWork.Employees.GetByIdAsync(employee.EmployeeId);
        return MapToDto(created!);
    }

    public async Task<EmployeeDto> UpdateAsync(Guid tenantId, Guid employeeId, UpdateEmployeeRequest request, CancellationToken ct)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId)
            ?? throw new NotFoundException(nameof(Employee), employeeId);
        EnsureEmployeeBelongsToTenant(tenantId, employee);

        var oldState = MapToDto(employee);

        if (request.Name is not null) employee.Name = request.Name;
        if (request.IsActive.HasValue) employee.IsActive = request.IsActive.Value;

        if (request.ServiceIds is not null)
        {
            var current = await _unitOfWork.EmployeeServices.GetByEmployeeIdAsync(employeeId);
            var currentIds = current.Select(es => es.ServiceId).ToHashSet();
            var newIds = request.ServiceIds.ToHashSet();
            if (newIds.Count > 200)
                throw new ArgumentException("An employee cannot have more than 200 services.");

            _unitOfWork.EmployeeServices.DeleteMany(current.Where(es => !newIds.Contains(es.ServiceId)));

            var addedServices = await _unitOfWork.Services.GetByIdsAsync(
                tenantId, newIds.Where(id => !currentIds.Contains(id)).ToArray(), ct);
            foreach (var service in addedServices)
            {
                await _unitOfWork.EmployeeServices.CreateAsync(new EmployeeService
                {
                    EmployeeServiceId = Guid.NewGuid(),
                    EmployeeId = employeeId,
                    ServiceId = service.ServiceId,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        employee.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Employees.UpdateAsync(employee);
        await _unitOfWork.SaveChangesAsync(ct);
        return MapToDto(employee);
    }

    public async Task DeactivateAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId)
            ?? throw new NotFoundException(nameof(Employee), employeeId);
        EnsureEmployeeBelongsToTenant(tenantId, employee);

        employee.IsActive = false;
        employee.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Employees.UpdateAsync(employee);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static void EnsureEmployeeBelongsToTenant(Guid tenantId, Employee employee)
    {
        if (employee.TenantId != tenantId)
            throw new NotFoundException(nameof(Employee), employee.EmployeeId);
    }

    private static EmployeeDto MapToDto(Employee e) => new(
        e.EmployeeId, e.TenantId, e.PartyId, e.Name, e.IsActive,
        e.EmployeeServices?.Select(es => es.ServiceId).ToList() ?? [],
        e.CreatedAt);
}
