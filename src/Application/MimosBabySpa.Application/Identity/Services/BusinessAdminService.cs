using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class BusinessAdminService : IBusinessAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<BusinessAdminService> _logger;

    public BusinessAdminService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<BusinessAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<BusinessDto> GetByIdAsync(Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);

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
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Businesses.CreateAsync(business);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Create", "Business", business.BusinessId.ToString(), null, business, ct);

        _logger.LogInformation("Business '{Name}' created for tenant {TenantId} [CorrelationId: {CorrelationId}]",
            business.Name, tenantId, _correlationIdProvider.CorrelationId);

        return MapToDto(business);
    }

    public async Task<BusinessDto> UpdateAsync(Guid businessId, UpdateBusinessRequest request, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);

        var oldState = MapToDto(business);

        if (request.Name is not null) business.Name = request.Name;
        if (request.Description is not null) business.Description = request.Description;
        if (request.Address is not null) business.Address = request.Address;
        if (request.Phone is not null) business.Phone = request.Phone;
        if (request.Email is not null) business.Email = request.Email;
        if (request.Website is not null) business.Website = request.Website;
        if (request.LogoUrl is not null) business.LogoUrl = request.LogoUrl;

        business.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Businesses.UpdateAsync(business);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Update", "Business", businessId.ToString(), oldState, MapToDto(business), ct);

        return MapToDto(business);
    }

    public async Task DeactivateAsync(Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);

        business.IsActive = false;
        business.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Businesses.UpdateAsync(business);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Deactivate", "Business", businessId.ToString(), null, null, ct);
    }

    private static BusinessDto MapToDto(Business b) => new(
        b.BusinessId, b.TenantId, b.Name, b.Description, b.Address,
        b.Phone, b.Email, b.Website, b.LogoUrl, b.IsActive, b.CreatedAt);
}

