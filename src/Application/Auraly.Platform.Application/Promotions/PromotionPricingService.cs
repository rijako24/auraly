using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Promotions;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Promotions;

public sealed class PromotionPricingService : IPromotionPricingService
{
    private readonly IUnitOfWork _unitOfWork;

    public PromotionPricingService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<PromotionPricingResult> EvaluateAsync(
        Guid businessId,
        IReadOnlyList<PromotionPricingItem> items,
        DateTime? utcNow = null,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return PromotionPricingResult.Empty(items);

        var promotions = await _unitOfWork.Promotions.GetActiveByBusinessIdAsync(
            businessId,
            utcNow ?? DateTime.UtcNow,
            ct);
        var resolved = PromotionPriceResolver.Resolve(
            items.Select(item => new PromotionPriceLineInput(
                item.Key, item.ItemType, item.ProductId, item.ServiceId, item.Name,
                item.CategoryName, item.UnitPrice, null, item.Quantity, string.Empty,
                IncludeInTotal: item.IncludeInTotal)).ToArray(),
            promotions.Select(ToRule).ToArray(),
            allowPromotionChannelCombination: false);
        var byKey = items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var priced = resolved.Lines.Select(line => new PromotionPricedItem(
            byKey[line.Input.Key],
            line.ReferenceUnitPrice * line.Input.Quantity,
            line.DiscountAmount,
            line.LineTotal,
            line.EffectiveUnitPrice,
            line.Adjustments.Select(adjustment => new PromotionAppliedAdjustment(
                adjustment.PromotionId, adjustment.PromotionName,
                adjustment.DiscountAmount, adjustment.Summary)).ToArray())).ToArray();
        return new PromotionPricingResult(priced, resolved.Subtotal, resolved.DiscountTotal, resolved.Total);
    }

    public async Task<PromotionPreview> PreviewAsync(
        Guid businessId,
        PromotionPricingItem item,
        DateTime? utcNow = null,
        CancellationToken ct = default)
    {
        var result = await EvaluateAsync(businessId, [item], utcNow, ct);
        var priced = result.Items.FirstOrDefault();
        if (priced is null || !priced.HasPromotion)
            return PromotionPreview.None(item.UnitPrice);

        return new PromotionPreview(
            item.UnitPrice,
            priced.EffectiveUnitPrice,
            priced.DiscountAmount,
            priced.Adjustments.First().PromotionId,
            priced.PromotionName,
            priced.PromotionSummary,
            true);
    }

    public static PromotionRule ToRule(Promotion promotion) => new(
        promotion.PromotionId,
        promotion.Name,
        promotion.Priority,
        promotion.IsCombinable,
        promotion.CouponCode,
        promotion.CreatedAt,
        promotion.Conditions.Select(condition => new PromotionConditionRule(
            condition.ItemType, condition.ProductId, condition.ServiceId,
            condition.CategoryName, condition.MinQuantity, condition.MinSubtotal)).ToArray(),
        promotion.Benefits.Select(benefit => new PromotionBenefitRule(
            benefit.BenefitType, benefit.TargetItemType, benefit.ProductId,
            benefit.ServiceId, benefit.CategoryName, benefit.DiscountPercentage,
            benefit.DiscountAmount, benefit.FixedUnitPrice, benefit.AppliesToQuantity)).ToArray());
}
