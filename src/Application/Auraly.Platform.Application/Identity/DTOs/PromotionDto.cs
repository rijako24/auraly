using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.DTOs;

public sealed record PromotionDto(
    Guid PromotionId,
    string Name,
    string? Description,
    bool IsActive,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    int Priority,
    bool IsCombinable,
    string? CouponCode,
    IReadOnlyList<PromotionConditionDto> Conditions,
    IReadOnlyList<PromotionBenefitDto> Benefits,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid TenantId,
    bool AppliesToAllBusinesses = false,
    IReadOnlyList<Guid>? ApplicableBusinessIds = null);

public sealed record PromotionConditionDto(
    Guid? PromotionConditionId,
    PromotionItemType ItemType,
    Guid? ProductId,
    Guid? ServiceId,
    string? CategoryName,
    decimal MinQuantity,
    decimal? MinSubtotal);

public sealed record PromotionBenefitDto(
    Guid? PromotionBenefitId,
    PromotionBenefitType BenefitType,
    PromotionItemType TargetItemType,
    Guid? ProductId,
    Guid? ServiceId,
    string? CategoryName,
    decimal? DiscountPercentage,
    decimal? DiscountAmount,
    decimal? FixedUnitPrice,
    decimal? AppliesToQuantity);

public sealed record CreatePromotionRequest(
    string Name,
    string? Description,
    bool IsActive,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    int Priority,
    bool IsCombinable,
    string? CouponCode,
    IReadOnlyList<PromotionConditionDto> Conditions,
    IReadOnlyList<PromotionBenefitDto> Benefits,
    bool AppliesToAllBusinesses = false,
    IReadOnlyList<Guid>? ApplicableBusinessIds = null);

public sealed record UpdatePromotionRequest(
    string? Name,
    string? Description,
    bool? IsActive,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    int? Priority,
    bool? IsCombinable,
    string? CouponCode,
    IReadOnlyList<PromotionConditionDto>? Conditions,
    IReadOnlyList<PromotionBenefitDto>? Benefits,
    bool? AppliesToAllBusinesses = null,
    IReadOnlyList<Guid>? ApplicableBusinessIds = null);
