using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class ServiceAdminService : IServiceAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<ServiceAdminService> _logger;

    public ServiceAdminService(
        IUnitOfWork unitOfWork,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<ServiceAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<ServiceDto> GetByIdAsync(Guid tenantId, Guid serviceId, CancellationToken ct)
    {
        var service = await _unitOfWork.Services.GetByIdAsync(serviceId)
            ?? throw new NotFoundException(nameof(Service), serviceId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, service.BusinessId, ct);
        return MapToDto(service);
    }

    public async Task<IReadOnlyList<ServiceDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var services = await _unitOfWork.Services.GetByBusinessIdAsync(businessId);
        return services.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<ServiceDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var (items, totalCount) = await _unitOfWork.Services.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, ct);
        return new PagedResponse<ServiceDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PagedResponse<ServiceCategoryDto>> GetPagedCategoriesByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var categories = (await _unitOfWork.ServiceCategories.GetByBusinessIdAsync(businessId))
            .Select(MapCategoryToDto)
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            categories = categories
                .Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var totalCount = categories.Count;
        var items = categories
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PagedResponse<ServiceCategoryDto>(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<ServiceDto> CreateAsync(Guid tenantId, CreateServiceRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, request.BusinessId, ct);

        var existing = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(request.BusinessId, request.ServiceName);
        if (existing is not null)
            throw new ConflictException($"Ya existe un servicio activo con el nombre '{request.ServiceName}' en este negocio.");

        var service = new Service
        {
            ServiceId = Guid.NewGuid(),
            BusinessId = request.BusinessId,
            ServiceName = request.ServiceName,
            Description = request.Description ?? string.Empty,
            Keywords = NormalizeKeywords(request.Keywords),
            DurationMinutes = request.DurationMinutes,
            Price = request.Price,
            IncludeInCheckoutTotal = request.IncludeInCheckoutTotal,
            IsActive = true,
            CategoryId = request.CategoryId,
            Tier = request.Tier,
            ServiceType = request.ServiceType,
            FulfillmentKind = request.FulfillmentKind,
            FixedScheduleLabel = NormalizeFixedScheduleLabel(request.FixedScheduleLabel),
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Services.CreateAsync(service);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Service '{Name}' created for business {BusinessId} [CorrelationId: {CorrelationId}]",
            service.ServiceName, request.BusinessId, _correlationIdProvider.CorrelationId);

        var created = await _unitOfWork.Services.GetByIdAsync(service.ServiceId);
        return MapToDto(created!);
    }

    public async Task<ServiceDto> UpdateAsync(Guid tenantId, Guid serviceId, UpdateServiceRequest request, CancellationToken ct)
    {
        var service = await _unitOfWork.Services.GetByIdAsync(serviceId)
            ?? throw new NotFoundException(nameof(Service), serviceId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, service.BusinessId, ct);

        var oldState = MapToDto(service);

        if (request.ServiceName is not null) service.ServiceName = request.ServiceName;
        if (request.Description is not null) service.Description = request.Description;
        if (request.Keywords is not null) service.Keywords = NormalizeKeywords(request.Keywords);
        if (request.DurationMinutes.HasValue) service.DurationMinutes = request.DurationMinutes.Value;
        if (request.Price.HasValue) service.Price = request.Price.Value;
        if (request.IncludeInCheckoutTotal.HasValue) service.IncludeInCheckoutTotal = request.IncludeInCheckoutTotal.Value;
        if (request.IsActive.HasValue) service.IsActive = request.IsActive.Value;
        if (request.CategoryId.HasValue) service.CategoryId = request.CategoryId.Value;
        if (request.Tier.HasValue) service.Tier = request.Tier.Value;
        if (request.ServiceType.HasValue) service.ServiceType = request.ServiceType.Value;
        if (request.FulfillmentKind.HasValue) service.FulfillmentKind = request.FulfillmentKind.Value;
        if (request.FixedScheduleLabel is not null) service.FixedScheduleLabel = NormalizeFixedScheduleLabel(request.FixedScheduleLabel);

        service.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Services.UpdateAsync(service);
        await _unitOfWork.SaveChangesAsync(ct);

        var updated = await _unitOfWork.Services.GetByIdAsync(serviceId);
        return MapToDto(updated!);
    }

    public async Task DeactivateAsync(Guid tenantId, Guid serviceId, CancellationToken ct)
    {
        var service = await _unitOfWork.Services.GetByIdAsync(serviceId)
            ?? throw new NotFoundException(nameof(Service), serviceId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, service.BusinessId, ct);

        service.IsActive = false;
        service.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Services.UpdateAsync(service);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static ServiceDto MapToDto(Service s) => new(
        s.ServiceId, s.BusinessId, s.ServiceName, s.Description, s.Keywords, s.DurationMinutes,
        s.Price, s.IncludeInCheckoutTotal, s.IsActive, s.CategoryId, s.ServiceCategory?.Name ?? string.Empty, s.Tier, s.ServiceType,
        s.FulfillmentKind, s.FixedScheduleLabel, s.CreatedAt);

    private static ServiceCategoryDto MapCategoryToDto(ServiceCategory c) => new(
        c.ServiceCategoryId, c.BusinessId, c.Name, c.DisplayOrder, c.IsActive, c.CreatedAt);

    private static string? NormalizeFixedScheduleLabel(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeKeywords(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
