using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Time;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Domain.Enums;
using Auraly.Contracts.Catalog;

namespace Auraly.Platform.Application.Identity.Services;

public class BusinessAdminService : IBusinessAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessDefaultsProvisioner _defaultsProvisioner;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<BusinessAdminService> _logger;
    private readonly IReferenceOptionStore _referenceOptions;
    private readonly TimeProvider _clock;

    public BusinessAdminService(
        IUnitOfWork unitOfWork,
        IBusinessDefaultsProvisioner defaultsProvisioner,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<BusinessAdminService> logger,
        IReferenceOptionStore referenceOptions,
        TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _defaultsProvisioner = defaultsProvisioner;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
        _referenceOptions = referenceOptions;
        _clock = clock;
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
        var mode = request.PointOfSaleMode ?? PointOfSaleModes.DirectSale;
        await ValidatePointOfSaleModeAsync(mode, ct);
        ValidateRestaurantWaiterPolicy(request.RestaurantRequireWaiterCredential,
            request.RestaurantRestrictTableToWaiter);
        ValidateSuggestedTip(request.RestaurantSuggestedTipPercent);
        if (mode != PointOfSaleModes.RestaurantBar && request.RestaurantSuggestedTipPercent != 0)
            throw new DomainValidationException(nameof(CreateBusinessRequest.RestaurantSuggestedTipPercent),
                "La propina sugerida solo aplica a sedes de restaurante y bar.");
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
            SharesProductPrices = request.SharesProductPrices,
            PointOfSaleMode = mode,
            RestaurantRequireWaiterCredential = request.RestaurantRequireWaiterCredential,
            RestaurantRestrictTableToWaiter = request.RestaurantRestrictTableToWaiter,
            RestaurantSuggestedTipPercent = mode == PointOfSaleModes.RestaurantBar
                ? request.RestaurantSuggestedTipPercent : 0,
            IsActive = true,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _unitOfWork.Businesses.CreateAsync(business);
            await _unitOfWork.SaveChangesAsync(ct);
            await _defaultsProvisioner.ProvisionWarehousesAsync(
                tenantId,
                business.BusinessId,
                request.PriceSourceBusinessId,
                await ReadTenantCostBasisAsync(tenantId, ct),
                ct);
            if (mode == PointOfSaleModes.RestaurantBar)
                await _defaultsProvisioner.ReconcileRestaurantRolePermissionsAsync(tenantId, ct);
        }, ct);

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
        if (request.PointOfSaleMode is not null && request.PointOfSaleMode != business.PointOfSaleMode)
            await ValidatePointOfSaleModeAsync(request.PointOfSaleMode, ct);
        ValidateRestaurantWaiterPolicy(
            request.RestaurantRequireWaiterCredential ?? business.RestaurantRequireWaiterCredential,
            request.RestaurantRestrictTableToWaiter ?? business.RestaurantRestrictTableToWaiter);
        if (request.RestaurantSuggestedTipPercent is { } suggestedTip)
        {
            ValidateSuggestedTip(suggestedTip);
            if ((request.PointOfSaleMode ?? business.PointOfSaleMode) != PointOfSaleModes.RestaurantBar &&
                suggestedTip != 0)
                throw new DomainValidationException(nameof(UpdateBusinessRequest.RestaurantSuggestedTipPercent),
                    "La propina sugerida solo aplica a sedes de restaurante y bar.");
        }

        if (request.Name is not null) business.Name = request.Name;
        if (request.Description is not null) business.Description = request.Description;
        if (request.Address is not null) business.Address = request.Address;
        if (request.Phone is not null) business.Phone = request.Phone;
        if (request.Email is not null) business.Email = request.Email;
        if (request.Website is not null) business.Website = request.Website;
        if (request.TimeZone is not null) business.TimeZone = NormalizeTimeZone(request.TimeZone);
        if (request.SharesProductPrices is not null) business.SharesProductPrices = request.SharesProductPrices.Value;
        if (request.PointOfSaleMode is not null) business.PointOfSaleMode = request.PointOfSaleMode;
        if (request.RestaurantRequireWaiterCredential is not null)
            business.RestaurantRequireWaiterCredential = request.RestaurantRequireWaiterCredential.Value;
        if (request.RestaurantRestrictTableToWaiter is not null)
            business.RestaurantRestrictTableToWaiter = request.RestaurantRestrictTableToWaiter.Value;
        if (request.RestaurantSuggestedTipPercent is not null)
            business.RestaurantSuggestedTipPercent = request.RestaurantSuggestedTipPercent.Value;
        if (business.PointOfSaleMode != PointOfSaleModes.RestaurantBar)
            business.RestaurantSuggestedTipPercent = 0;

        business.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _unitOfWork.Businesses.UpdateAsync(business);
            await _unitOfWork.SaveChangesAsync(ct);
            if (request.PointOfSaleMode is not null)
                await _defaultsProvisioner.ReconcileRestaurantRolePermissionsAsync(business.TenantId, ct);
        }, ct);

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
        business.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _unitOfWork.Businesses.UpdateAsync(business);
            await _unitOfWork.SaveChangesAsync(ct);
            if (business.PointOfSaleMode == PointOfSaleModes.RestaurantBar)
                await _defaultsProvisioner.ReconcileRestaurantRolePermissionsAsync(business.TenantId, ct);
        }, ct);
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

    private static void ValidateRestaurantWaiterPolicy(bool requireCredential, bool restrictTable)
    {
        if (restrictTable && !requireCredential)
            throw new DomainValidationException(nameof(UpdateBusinessRequest.RestaurantRequireWaiterCredential),
                "Para restringir una mesa a su mesero responsable debes pedir la clave al seleccionar mesero.");
    }

    private static void ValidateSuggestedTip(decimal percent)
    {
        if (percent < 0 || percent > 10 || decimal.Round(percent, 2) != percent)
            throw new DomainValidationException(nameof(UpdateBusinessRequest.RestaurantSuggestedTipPercent),
                "La propina sugerida debe estar entre 0 % y 10 %, con máximo dos decimales.");
    }

    private async Task ValidatePointOfSaleModeAsync(string mode, CancellationToken ct)
    {
        if (!PointOfSaleModes.IsSupported(mode) ||
            !(await _referenceOptions.ListAsync(PointOfSaleModes.CatalogCode, ct))
                .Any(option => option.Code == mode))
            throw new DomainValidationException(nameof(CreateBusinessRequest.PointOfSaleMode),
                "Selecciona un modo de punto de venta vigente.");
    }

    private async Task<string> ReadTenantCostBasisAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        return tenant.InventoryCostBasis;
    }

    private static BusinessDto MapToDto(Business b) => new(
        b.BusinessId, b.TenantId, b.Name, b.Description, b.Address,
        b.Phone, b.Email, b.Website, b.TimeZone, b.SharesProductPrices, b.IsActive, b.CreatedAt,
        b.PointOfSaleMode, b.RestaurantRequireWaiterCredential,
        b.RestaurantRestrictTableToWaiter, b.RestaurantSuggestedTipPercent);
}
