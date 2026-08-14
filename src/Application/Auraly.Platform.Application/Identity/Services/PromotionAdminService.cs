using Microsoft.Extensions.Logging;
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
    private readonly IAuditService _auditService;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<PromotionAdminService> _logger;

    public PromotionAdminService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<PromotionAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
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

    public async Task<PromotionDto> CreateAsync(Guid tenantId, CreatePromotionRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, request.BusinessId, ct);
        Validate(request.Name, request.StartsAtUtc, request.EndsAtUtc, request.Benefits);

        var promotion = new Promotion
        {
            PromotionId = Guid.NewGuid(),
            BusinessId = request.BusinessId,
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            IsActive = request.IsActive,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            Priority = request.Priority,
            IsCombinable = request.IsCombinable,
            CouponCode = NormalizeCoupon(request.CouponCode),
            CreatedAt = DateTime.UtcNow
        };

        foreach (var condition in request.Conditions ?? [])
            promotion.Conditions.Add(MapCondition(request.BusinessId, promotion.PromotionId, condition));
        foreach (var benefit in request.Benefits ?? [])
            promotion.Benefits.Add(MapBenefit(request.BusinessId, promotion.PromotionId, benefit));

        await _unitOfWork.Promotions.CreateAsync(promotion, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditService.LogAsync("Create", "Promotion", promotion.PromotionId.ToString(), null, Map(promotion), ct);
        _logger.LogInformation("Promotion '{Name}' created for business {BusinessId} [CorrelationId: {CorrelationId}]",
            promotion.Name, promotion.BusinessId, _correlationIdProvider.CorrelationId);
        return Map(promotion);
    }

    public async Task<PromotionDto> UpdateAsync(Guid tenantId, Guid businessId, Guid promotionId, UpdatePromotionRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var promotion = await _unitOfWork.Promotions.GetByIdAsync(businessId, promotionId, ct)
            ?? throw new NotFoundException(nameof(Promotion), promotionId);
        var oldState = Map(promotion);

        var finalName = request.Name ?? promotion.Name;
        var finalStarts = request.StartsAtUtc ?? promotion.StartsAtUtc;
        var finalEnds = request.EndsAtUtc ?? promotion.EndsAtUtc;
        var finalBenefits = request.Benefits ?? promotion.Benefits.Select(MapBenefitDto).ToList();
        Validate(finalName, finalStarts, finalEnds, finalBenefits);

        if (request.Name is not null) promotion.Name = request.Name.Trim();
        if (request.Description is not null) promotion.Description = Normalize(request.Description);
        if (request.IsActive.HasValue) promotion.IsActive = request.IsActive.Value;
        if (request.StartsAtUtc.HasValue || request.StartsAtUtc is null) promotion.StartsAtUtc = request.StartsAtUtc;
        if (request.EndsAtUtc.HasValue || request.EndsAtUtc is null) promotion.EndsAtUtc = request.EndsAtUtc;
        if (request.Priority.HasValue) promotion.Priority = request.Priority.Value;
        if (request.IsCombinable.HasValue) promotion.IsCombinable = request.IsCombinable.Value;
        if (request.CouponCode is not null) promotion.CouponCode = NormalizeCoupon(request.CouponCode);

        if (request.Conditions is not null)
        {
            promotion.Conditions.Clear();
            foreach (var condition in request.Conditions)
                promotion.Conditions.Add(MapCondition(businessId, promotionId, condition));
        }

        if (request.Benefits is not null)
        {
            promotion.Benefits.Clear();
            foreach (var benefit in request.Benefits)
                promotion.Benefits.Add(MapBenefit(businessId, promotionId, benefit));
        }

        promotion.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Promotions.UpdateAsync(promotion, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        var updated = await _unitOfWork.Promotions.GetByIdAsync(businessId, promotionId, ct) ?? promotion;
        await _auditService.LogAsync("Update", "Promotion", promotionId.ToString(), oldState, Map(updated), ct);
        return Map(updated);
    }

    public async Task DeactivateAsync(Guid tenantId, Guid businessId, Guid promotionId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var promotion = await _unitOfWork.Promotions.GetByIdAsync(businessId, promotionId, ct)
            ?? throw new NotFoundException(nameof(Promotion), promotionId);
        promotion.IsActive = false;
        promotion.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Promotions.UpdateAsync(promotion, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditService.LogAsync("Deactivate", "Promotion", promotionId.ToString(), null, null, ct);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static void Validate(string name, DateTime? startsAtUtc, DateTime? endsAtUtc, IReadOnlyList<PromotionBenefitDto>? benefits)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Name", "El nombre de la promocion es obligatorio.");
        if (startsAtUtc.HasValue && endsAtUtc.HasValue && startsAtUtc > endsAtUtc)
            throw new DomainValidationException("StartsAtUtc", "La fecha inicial no puede ser posterior a la fecha final.");
        if (benefits is null || benefits.Count == 0)
            throw new DomainValidationException("Benefits", "La promocion debe tener al menos un beneficio.");

        foreach (var benefit in benefits)
        {
            if (benefit.BenefitType == PromotionBenefitType.PercentageDiscount && (benefit.DiscountPercentage is null or <= 0 or > 100))
                throw new DomainValidationException("DiscountPercentage", "El porcentaje de descuento debe estar entre 0 y 100.");
            if (benefit.BenefitType == PromotionBenefitType.AmountDiscount && (benefit.DiscountAmount is null or <= 0))
                throw new DomainValidationException("DiscountAmount", "El descuento fijo debe ser mayor a cero.");
            if (benefit.BenefitType == PromotionBenefitType.FixedUnitPrice && (benefit.FixedUnitPrice is null or < 0))
                throw new DomainValidationException("FixedUnitPrice", "El precio fijo no puede ser negativo.");
        }
    }

    private static PromotionCondition MapCondition(Guid businessId, Guid promotionId, PromotionConditionDto dto) => new()
    {
        PromotionConditionId = dto.PromotionConditionId ?? Guid.NewGuid(),
        PromotionId = promotionId,
        BusinessId = businessId,
        ItemType = dto.ItemType,
        ProductId = dto.ProductId,
        ServiceId = dto.ServiceId,
        CategoryName = Normalize(dto.CategoryName),
        MinQuantity = dto.MinQuantity <= 0 ? 1 : dto.MinQuantity,
        MinSubtotal = dto.MinSubtotal,
        CreatedAt = DateTime.UtcNow
    };

    private static PromotionBenefit MapBenefit(Guid businessId, Guid promotionId, PromotionBenefitDto dto) => new()
    {
        PromotionBenefitId = dto.PromotionBenefitId ?? Guid.NewGuid(),
        PromotionId = promotionId,
        BusinessId = businessId,
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
        p.BusinessId,
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
        p.UpdatedAt);

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

