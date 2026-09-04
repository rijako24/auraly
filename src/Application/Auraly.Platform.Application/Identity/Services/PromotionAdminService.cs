using Microsoft.Extensions.Logging;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class PromotionAdminService : IPromotionAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<PromotionAdminService> _logger;
    private readonly IPosPricingSynchronizationWriter _pricingSynchronization;
    private readonly IPosSynchronizationOutboxDispatcher _synchronization;

    public PromotionAdminService(
        IUnitOfWork unitOfWork,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<PromotionAdminService> logger,
        IPosPricingSynchronizationWriter pricingSynchronization,
        IPosSynchronizationOutboxDispatcher synchronization)
    {
        _unitOfWork = unitOfWork;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
        _pricingSynchronization = pricingSynchronization;
        _synchronization = synchronization;
    }

    public async Task<PromotionDto> GetByIdAsync(Guid tenantId, Guid businessId, Guid promotionId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var promotion = await _unitOfWork.Promotions.GetByIdAsync(businessId, promotionId, ct)
            ?? throw new NotFoundException(nameof(Promotion), promotionId);
        return Map(promotion);
    }

    public async Task<PagedResponse<PromotionDto>> GetPagedByBusinessIdAsync(Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var (items, totalCount) = await _unitOfWork.Promotions.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, ct);
        return new PagedResponse<PromotionDto>(items.Select(Map).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PromotionDto> CreateAsync(
        Guid tenantId, Guid businessId, CreatePromotionRequest request, CancellationToken ct = default)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
            Validate(request.Name, request.StartsAtUtc, request.EndsAtUtc, request.Conditions, request.Benefits);
            var businessIds = await ValidateScopeAsync(
                tenantId, businessId, request.AppliesToAllBusinesses,
                request.ApplicableBusinessIds, ct);
            var affectedBusinessIds = await ResolveAffectedBusinessIdsAsync(
                tenantId, request.AppliesToAllBusinesses, businessIds, ct);
            var promotion = new Promotion
            {
                PromotionId = Guid.NewGuid(),
                TenantId = tenantId,
                Name = request.Name.Trim(),
                Description = Normalize(request.Description),
                IsActive = request.IsActive,
                StartsAtUtc = request.StartsAtUtc,
                EndsAtUtc = request.EndsAtUtc,
                Priority = request.Priority,
                IsCombinable = request.IsCombinable,
                AppliesToAllBusinesses = request.AppliesToAllBusinesses,
                CouponCode = NormalizeCoupon(request.CouponCode),
                CreatedAt = DateTime.UtcNow
            };
            foreach (var condition in request.Conditions ?? [])
                promotion.Conditions.Add(MapCondition(tenantId, promotion.PromotionId, condition));
            foreach (var benefit in request.Benefits ?? [])
                promotion.Benefits.Add(MapBenefit(tenantId, promotion.PromotionId, benefit));
            foreach (var scopedBusinessId in businessIds)
                promotion.BusinessScopes.Add(new PromotionBusinessScope
                {
                    PromotionId = promotion.PromotionId,
                    BusinessId = scopedBusinessId,
                    TenantId = tenantId
                });
            await _unitOfWork.Promotions.CreateAsync(promotion, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _pricingSynchronization.EnqueueBusinessesAsync(affectedBusinessIds, ct);
            return (Promotion: promotion, BusinessIds: affectedBusinessIds);
        }, ct);
        _logger.LogInformation(
            "Promotion '{Name}' created for tenant {TenantId} with business scope {BusinessScope} [CorrelationId: {CorrelationId}]",
            result.Promotion.Name, tenantId,
            result.Promotion.AppliesToAllBusinesses ? "all" : string.Join(',', result.BusinessIds),
            _correlationIdProvider.CorrelationId);
        await DispatchAsync(tenantId, result.BusinessIds);
        return Map(result.Promotion);
    }

    public async Task<PromotionDto> UpdateAsync(Guid tenantId, Guid businessId, Guid promotionId, UpdatePromotionRequest request, CancellationToken ct = default)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
            var promotion = await _unitOfWork.Promotions.GetByIdAsync(businessId, promotionId, ct)
                ?? throw new NotFoundException(nameof(Promotion), promotionId);
            var oldBusinessIds = await ResolveAffectedBusinessIdsAsync(
                tenantId, promotion.AppliesToAllBusinesses,
                promotion.BusinessScopes.Select(scope => scope.BusinessId).ToArray(), ct);
            var finalName = request.Name ?? promotion.Name;
            var finalStarts = request.StartsAtUtc ?? promotion.StartsAtUtc;
            var finalEnds = request.EndsAtUtc ?? promotion.EndsAtUtc;
            var finalConditions = request.Conditions ?? promotion.Conditions.Select(MapConditionDto).ToList();
            var finalBenefits = request.Benefits ?? promotion.Benefits.Select(MapBenefitDto).ToList();
            Validate(finalName, finalStarts, finalEnds, finalConditions, finalBenefits);

            if (request.Name is not null) promotion.Name = request.Name.Trim();
            if (request.Description is not null) promotion.Description = Normalize(request.Description);
            if (request.IsActive.HasValue) promotion.IsActive = request.IsActive.Value;
            if (request.StartsAtUtc.HasValue || request.StartsAtUtc is null) promotion.StartsAtUtc = request.StartsAtUtc;
            if (request.EndsAtUtc.HasValue || request.EndsAtUtc is null) promotion.EndsAtUtc = request.EndsAtUtc;
            if (request.Priority.HasValue) promotion.Priority = request.Priority.Value;
            if (request.IsCombinable.HasValue) promotion.IsCombinable = request.IsCombinable.Value;
            if (request.CouponCode is not null) promotion.CouponCode = NormalizeCoupon(request.CouponCode);

            if (request.AppliesToAllBusinesses.HasValue || request.ApplicableBusinessIds is not null)
            {
                var appliesToAll = request.AppliesToAllBusinesses ?? promotion.AppliesToAllBusinesses;
                var requestedBusinessIds = request.ApplicableBusinessIds
                    ?? promotion.BusinessScopes.Select(scope => scope.BusinessId).ToArray();
                var businessIds = await ValidateScopeAsync(
                    tenantId, businessId, appliesToAll, requestedBusinessIds, ct);
                promotion.AppliesToAllBusinesses = appliesToAll;
                promotion.BusinessScopes.Clear();
                foreach (var scopedBusinessId in businessIds)
                    promotion.BusinessScopes.Add(new PromotionBusinessScope
                    {
                        PromotionId = promotion.PromotionId,
                        BusinessId = scopedBusinessId,
                        TenantId = tenantId
                    });
            }

            if (request.Conditions is not null)
            {
                promotion.Conditions.Clear();
                foreach (var condition in request.Conditions)
                    promotion.Conditions.Add(MapCondition(tenantId, promotionId, condition));
            }

            if (request.Benefits is not null)
            {
                promotion.Benefits.Clear();
                foreach (var benefit in request.Benefits)
                    promotion.Benefits.Add(MapBenefit(tenantId, promotionId, benefit));
            }

            promotion.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Promotions.UpdateAsync(promotion, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            var newBusinessIds = await ResolveAffectedBusinessIdsAsync(
                tenantId, promotion.AppliesToAllBusinesses,
                promotion.BusinessScopes.Select(scope => scope.BusinessId).ToArray(), ct);
            var affectedBusinessIds = oldBusinessIds.Concat(newBusinessIds).Distinct().ToArray();
            await _pricingSynchronization.EnqueueBusinessesAsync(affectedBusinessIds, ct);
            var updated = await _unitOfWork.Promotions.GetByIdAsync(businessId, promotionId, ct)
                ?? promotion;
            return (Promotion: updated, BusinessIds: affectedBusinessIds);
        }, ct);
        await DispatchAsync(tenantId, result.BusinessIds);
        return Map(result.Promotion);
    }

    public async Task DeactivateAsync(Guid tenantId, Guid businessId, Guid promotionId, CancellationToken ct = default)
    {
        var businessIds = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
            var promotion = await _unitOfWork.Promotions.GetByIdAsync(businessId, promotionId, ct)
                ?? throw new NotFoundException(nameof(Promotion), promotionId);
            var affectedBusinessIds = await ResolveAffectedBusinessIdsAsync(
                tenantId, promotion.AppliesToAllBusinesses,
                promotion.BusinessScopes.Select(scope => scope.BusinessId).ToArray(), ct);
            promotion.IsActive = false;
            promotion.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Promotions.UpdateAsync(promotion, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _pricingSynchronization.EnqueueBusinessesAsync(affectedBusinessIds, ct);
            return affectedBusinessIds;
        }, ct);
        await DispatchAsync(tenantId, businessIds);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static void Validate(
        string name,
        DateTime? startsAtUtc,
        DateTime? endsAtUtc,
        IReadOnlyList<PromotionConditionDto>? conditions,
        IReadOnlyList<PromotionBenefitDto>? benefits)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Name", "El nombre de la promocion es obligatorio.");
        if (startsAtUtc.HasValue && endsAtUtc.HasValue && startsAtUtc > endsAtUtc)
            throw new DomainValidationException("StartsAtUtc", "La fecha inicial no puede ser posterior a la fecha final.");
        if (benefits is null || benefits.Count == 0)
            throw new DomainValidationException("Benefits", "La promocion debe tener al menos un beneficio.");

        foreach (var condition in conditions ?? [])
        {
            ValidateTarget(condition.ItemType, condition.ProductId, condition.ServiceId,
                condition.CategoryName, "Conditions");
            if (condition.MinQuantity <= 0)
                throw new DomainValidationException("MinQuantity", "La cantidad mínima debe ser mayor a cero.");
            if (condition.MinSubtotal is < 0)
                throw new DomainValidationException("MinSubtotal", "El subtotal mínimo no puede ser negativo.");
        }

        foreach (var benefit in benefits)
        {
            ValidateTarget(benefit.TargetItemType, benefit.ProductId, benefit.ServiceId,
                benefit.CategoryName, "Benefits");
            if (benefit.BenefitType == PromotionBenefitType.PercentageDiscount && (benefit.DiscountPercentage is null or <= 0 or > 100))
                throw new DomainValidationException("DiscountPercentage", "El porcentaje de descuento debe estar entre 0 y 100.");
            if (benefit.BenefitType == PromotionBenefitType.AmountDiscount && (benefit.DiscountAmount is null or <= 0))
                throw new DomainValidationException("DiscountAmount", "El descuento fijo debe ser mayor a cero.");
            if (benefit.BenefitType == PromotionBenefitType.FixedUnitPrice && (benefit.FixedUnitPrice is null or < 0))
                throw new DomainValidationException("FixedUnitPrice", "El precio fijo no puede ser negativo.");
            if (benefit.BenefitType == PromotionBenefitType.FreeItem && benefit.AppliesToQuantity is null or <= 0)
                throw new DomainValidationException("AppliesToQuantity", "La cantidad gratuita debe ser mayor a cero.");
        }
    }

    private async Task<Guid[]> ValidateScopeAsync(
        Guid tenantId,
        Guid currentBusinessId,
        bool appliesToAllBusinesses,
        IReadOnlyList<Guid>? requestedBusinessIds,
        CancellationToken ct)
    {
        if (appliesToAllBusinesses)
            return [];
        var businessIds = (requestedBusinessIds ?? [currentBusinessId])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (businessIds.Length == 0)
            throw new DomainValidationException(
                "ApplicableBusinessIds", "Selecciona al menos una sede o habilita todas las sedes.");
        foreach (var scopedBusinessId in businessIds)
            await EnsureBusinessBelongsToTenantAsync(tenantId, scopedBusinessId, ct);
        return businessIds;
    }

    private async Task<Guid[]> ResolveAffectedBusinessIdsAsync(
        Guid tenantId,
        bool appliesToAllBusinesses,
        IReadOnlyCollection<Guid> scopedBusinessIds,
        CancellationToken ct)
    {
        if (!appliesToAllBusinesses)
            return scopedBusinessIds.Distinct().ToArray();
        return (await _unitOfWork.Businesses.GetByTenantIdAsync(tenantId, ct))
            .Where(business => business.IsActive)
            .Select(business => business.BusinessId)
            .ToArray();
    }

    private async Task DispatchAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> businessIds)
    {
        foreach (var affectedBusinessId in businessIds.Distinct())
            await _synchronization.DispatchPendingAsync(
                tenantId, affectedBusinessId, CancellationToken.None);
    }

    private static void ValidateTarget(
        PromotionItemType itemType, Guid? productId, Guid? serviceId, string? categoryName, string field)
    {
        var valid = itemType switch
        {
            PromotionItemType.Product => productId.HasValue && productId != Guid.Empty,
            PromotionItemType.Service => serviceId.HasValue && serviceId != Guid.Empty,
            PromotionItemType.ProductCategory or PromotionItemType.ServiceCategory =>
                !string.IsNullOrWhiteSpace(categoryName),
            PromotionItemType.Any or PromotionItemType.AnyProduct or PromotionItemType.AnyService => true,
            _ => false
        };
        if (!valid)
            throw new DomainValidationException(field,
                "La regla debe identificar el producto, servicio o categoría que utiliza.");
    }

    private static PromotionCondition MapCondition(Guid tenantId, Guid promotionId, PromotionConditionDto dto) => new()
    {
        PromotionConditionId = dto.PromotionConditionId ?? Guid.NewGuid(),
        PromotionId = promotionId,
        TenantId = tenantId,
        ItemType = dto.ItemType,
        ProductId = dto.ProductId,
        ServiceId = dto.ServiceId,
        CategoryName = Normalize(dto.CategoryName),
        MinQuantity = dto.MinQuantity <= 0 ? 1 : dto.MinQuantity,
        MinSubtotal = dto.MinSubtotal,
        CreatedAt = DateTime.UtcNow
    };

    private static PromotionBenefit MapBenefit(Guid tenantId, Guid promotionId, PromotionBenefitDto dto) => new()
    {
        PromotionBenefitId = dto.PromotionBenefitId ?? Guid.NewGuid(),
        PromotionId = promotionId,
        TenantId = tenantId,
        BenefitType = dto.BenefitType,
        TargetItemType = dto.TargetItemType,
        ProductId = dto.ProductId,
        ServiceId = dto.ServiceId,
        CategoryName = Normalize(dto.CategoryName),
        DiscountPercentage = dto.DiscountPercentage,
        DiscountAmount = dto.DiscountAmount,
        FixedUnitPrice = dto.FixedUnitPrice,
        AppliesToQuantity = dto.AppliesToQuantity,
        CreatedAt = DateTime.UtcNow
    };

    private static PromotionDto Map(Promotion p) => new(
        p.PromotionId,
        p.Name,
        p.Description,
        p.IsActive,
        p.StartsAtUtc,
        p.EndsAtUtc,
        p.Priority,
        p.IsCombinable,
        p.CouponCode,
        p.Conditions.Select(MapConditionDto).ToList(),
        p.Benefits.Select(MapBenefitDto).ToList(),
        p.CreatedAt,
        p.UpdatedAt,
        p.TenantId,
        p.AppliesToAllBusinesses,
        p.BusinessScopes.Count == 0 && !p.AppliesToAllBusinesses
            ? []
            : p.BusinessScopes.Select(scope => scope.BusinessId).Order().ToArray());

    private static PromotionConditionDto MapConditionDto(PromotionCondition c) => new(
        c.PromotionConditionId,
        c.ItemType,
        c.ProductId,
        c.ServiceId,
        c.CategoryName,
        c.MinQuantity,
        c.MinSubtotal);

    private static PromotionBenefitDto MapBenefitDto(PromotionBenefit b) => new(
        b.PromotionBenefitId,
        b.BenefitType,
        b.TargetItemType,
        b.ProductId,
        b.ServiceId,
        b.CategoryName,
        b.DiscountPercentage,
        b.DiscountAmount,
        b.FixedUnitPrice,
        b.AppliesToQuantity);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeCoupon(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

