using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Time;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class BusinessAdminService : IBusinessAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly IBusinessDefaultsProvisioner _defaultsProvisioner;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<BusinessAdminService> _logger;

    public BusinessAdminService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        IBusinessDefaultsProvisioner defaultsProvisioner,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<BusinessAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _defaultsProvisioner = defaultsProvisioner;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<BusinessDto> GetByIdAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid businessId,
        CancellationToken ct)
    {
        var business = await GetBusinessForScopeAsync(tenantId, canAccessAllTenants, businessId);
        return MapToDto(business);
    }

    public async Task<IReadOnlyList<BusinessDto>> GetByTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var businesses = await _unitOfWork.Businesses.GetByTenantIdAsync(tenantId, ct);
        return businesses.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<BusinessDto>> GetPagedByTenantAsync(
        Guid tenantId, PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await _unitOfWork.Businesses.GetPagedByTenantIdAsync(
            tenantId, request.Page, request.PageSize, request.Search, ct);
        return new PagedResponse<BusinessDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PagedResponse<BusinessDto>> GetPagedAsync(PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await _unitOfWork.Businesses.GetPagedAsync(
            request.Page, request.PageSize, request.Search, ct);
        return new PagedResponse<BusinessDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<BusinessDto> CreateAsync(Guid tenantId, CreateBusinessRequest request, CancellationToken ct)
    {
        var business = new Business
        {
            BusinessId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Address = request.Address ?? string.Empty,
            Phone = request.Phone ?? string.Empty,
            Email = request.Email ?? string.Empty,
            Website = request.Website ?? string.Empty,
            TimeZone = NormalizeTimeZone(request.TimeZone),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _unitOfWork.Businesses.CreateAsync(business);
            await _unitOfWork.SaveChangesAsync(ct);
            await _defaultsProvisioner.ProvisionWarehousesAsync(
                tenantId,
                business.BusinessId,
                "LatestReceiptCost",
                ct);
        }, ct);

        await _auditService.LogAsync("Create", "Business", business.BusinessId.ToString(), null, business, ct);

        _logger.LogInformation("Business '{Name}' created for tenant {TenantId} [CorrelationId: {CorrelationId}]",
            business.Name, tenantId, _correlationIdProvider.CorrelationId);

        return MapToDto(business);
    }

    public async Task<BusinessDto> UpdateAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid businessId,
        UpdateBusinessRequest request,
        CancellationToken ct)
    {
        var business = await GetBusinessForScopeAsync(tenantId, canAccessAllTenants, businessId);
        var oldState = MapToDto(business);

        if (request.Name is not null) business.Name = request.Name;
        if (request.Description is not null) business.Description = request.Description;
        if (request.Address is not null) business.Address = request.Address;
        if (request.Phone is not null) business.Phone = request.Phone;
        if (request.Email is not null) business.Email = request.Email;
        if (request.Website is not null) business.Website = request.Website;
        if (request.LogoUrl is not null) business.LogoUrl = request.LogoUrl;
        if (request.TimeZone is not null) business.TimeZone = NormalizeTimeZone(request.TimeZone);

        business.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Businesses.UpdateAsync(business);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Update", "Business", businessId.ToString(), oldState, MapToDto(business), ct);

        return MapToDto(business);
    }

    public async Task DeactivateAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid businessId,
        CancellationToken ct)
    {
        var business = await GetBusinessForScopeAsync(tenantId, canAccessAllTenants, businessId);

        business.IsActive = false;
        business.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Businesses.UpdateAsync(business);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Deactivate", "Business", businessId.ToString(), null, null, ct);
    }

    private async Task<Business> GetBusinessForScopeAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid businessId)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);

        if (!canAccessAllTenants && business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);

        return business;
    }

    private static string NormalizeTimeZone(string? timeZone) =>
        string.IsNullOrWhiteSpace(timeZone) ? BusinessClock.DefaultTimeZoneId : timeZone.Trim();

    private static BusinessDto MapToDto(Business b) => new(
        b.BusinessId, b.TenantId, b.Name, b.Description, b.Address,
        b.Phone, b.Email, b.Website, b.LogoUrl, b.TimeZone, b.IsActive, b.CreatedAt);
}
