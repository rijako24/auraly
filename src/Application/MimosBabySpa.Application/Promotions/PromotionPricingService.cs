using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Promotions;

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

        if (promotions.Count == 0)
            return PromotionPricingResult.Empty(items);

        var state = items.ToDictionary(
            i => i.Key,
            i => new MutablePricedItem(i, i.UnitPrice * i.Quantity),
            StringComparer.OrdinalIgnoreCase);

        foreach (var promotion in SelectEligiblePromotions(promotions, items))
        {
            var applied = false;
            foreach (var benefit in promotion.Benefits)
            {
                var candidates = state.Values
                    .Where(i => MatchesBenefit(benefit, i.Item))
                    .OrderByDescending(i => i.Item.UnitPrice)
                    .ToList();

                foreach (var candidate in candidates)
                {
                    var discount = CalculateDiscount(benefit, candidate);
                    if (discount <= 0)
                        continue;

                    candidate.DiscountAmount += discount;
                    candidate.Adjustments.Add(new PromotionAppliedAdjustment(
                        promotion.PromotionId,
                        promotion.Name,
                        discount,
                        BuildSummary(promotion, benefit)));
                    applied = true;

                    if (!promotion.IsCombinable)
                        break;
                }

                if (applied && !promotion.IsCombinable)
                    break;
            }

            if (applied && !promotion.IsCombinable)
                break;
        }

        var priced = state.Values
            .Select(i =>
            {
                var discount = Math.Min(i.DiscountAmount, i.LineSubtotal);
                var total = i.LineSubtotal - discount;
                var effectiveUnitPrice = i.Item.Quantity > 0 ? total / i.Item.Quantity : i.Item.UnitPrice;
                return new PromotionPricedItem(
                    i.Item,
                    i.LineSubtotal,
                    discount,
                    total,
                    effectiveUnitPrice,
                    i.Adjustments);
            })
            .ToList();

        var subtotal = priced.Where(i => i.Item.IncludeInTotal).Sum(i => i.LineSubtotal);
        var discountTotal = priced.Where(i => i.Item.IncludeInTotal).Sum(i => i.DiscountAmount);
        return new PromotionPricingResult(priced, subtotal, discountTotal, subtotal - discountTotal);
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

    private static IEnumerable<Promotion> SelectEligiblePromotions(
        IReadOnlyList<Promotion> promotions,
        IReadOnlyList<PromotionPricingItem> items)
    {
        foreach (var promotion in promotions)
        {
            if (promotion.Conditions.Count == 0 || promotion.Conditions.All(c => MatchesCondition(c, items)))
                yield return promotion;
        }
    }

    private static bool MatchesCondition(PromotionCondition condition, IReadOnlyList<PromotionPricingItem> items)
    {
        var matches = items.Where(item => MatchesTarget(condition.ItemType, condition.ProductId, condition.ServiceId, condition.CategoryName, item)).ToList();
        if (matches.Sum(i => i.Quantity) < condition.MinQuantity)
            return false;

        return condition.MinSubtotal is null || matches.Sum(i => i.UnitPrice * i.Quantity) >= condition.MinSubtotal.Value;
    }

    private static bool MatchesBenefit(PromotionBenefit benefit, PromotionPricingItem item) =>
        MatchesTarget(benefit.TargetItemType, benefit.ProductId, benefit.ServiceId, benefit.CategoryName, item);

    private static bool MatchesTarget(
        PromotionItemType targetType,
        Guid? productId,
        Guid? serviceId,
        string? categoryName,
        PromotionPricingItem item)
    {
        return targetType switch
        {
            PromotionItemType.Any => true,
            PromotionItemType.AnyProduct => item.ItemType == PromotionItemType.Product,
            PromotionItemType.AnyService => item.ItemType == PromotionItemType.Service,
            PromotionItemType.Product => item.ItemType == PromotionItemType.Product && productId == item.ProductId,
            PromotionItemType.Service => item.ItemType == PromotionItemType.Service && serviceId == item.ServiceId,
            PromotionItemType.ProductCategory => item.ItemType == PromotionItemType.Product && Same(categoryName, item.CategoryName),
            PromotionItemType.ServiceCategory => item.ItemType == PromotionItemType.Service && Same(categoryName, item.CategoryName),
            _ => false
        };
    }

    private static decimal CalculateDiscount(PromotionBenefit benefit, MutablePricedItem item)
    {
        var remaining = Math.Max(0, item.LineSubtotal - item.DiscountAmount);
        if (remaining <= 0)
            return 0;

        var quantity = benefit.AppliesToQuantity.HasValue
            ? Math.Min(item.Item.Quantity, benefit.AppliesToQuantity.Value)
            : item.Item.Quantity;
        if (quantity <= 0)
            return 0;

        var eligibleSubtotal = Math.Min(remaining, item.Item.UnitPrice * quantity);
        var discount = benefit.BenefitType switch
        {
            PromotionBenefitType.PercentageDiscount =>
                eligibleSubtotal * Math.Clamp(benefit.DiscountPercentage ?? 0, 0, 100) / 100,
            PromotionBenefitType.AmountDiscount =>
                Math.Min(eligibleSubtotal, benefit.DiscountAmount ?? 0),
            PromotionBenefitType.FixedUnitPrice =>
                Math.Max(0, (item.Item.UnitPrice - (benefit.FixedUnitPrice ?? item.Item.UnitPrice)) * quantity),
            PromotionBenefitType.FreeItem =>
                eligibleSubtotal,
            _ => 0
        };

        return Math.Round(Math.Min(discount, remaining), 2, MidpointRounding.AwayFromZero);
    }

    private static string BuildSummary(Promotion promotion, PromotionBenefit benefit)
    {
        var benefitText = benefit.BenefitType switch
        {
            PromotionBenefitType.PercentageDiscount => $"{benefit.DiscountPercentage:0.##}% de descuento",
            PromotionBenefitType.AmountDiscount => $"descuento de {benefit.DiscountAmount:0.##}",
            PromotionBenefitType.FixedUnitPrice => $"precio promocional {benefit.FixedUnitPrice:0.##}",
            PromotionBenefitType.FreeItem => "item gratis",
            _ => "promocion aplicada"
        };

        return $"{promotion.Name}: {benefitText}";
    }

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class MutablePricedItem
    {
        public MutablePricedItem(PromotionPricingItem item, decimal lineSubtotal)
        {
            Item = item;
            LineSubtotal = lineSubtotal;
        }

        public PromotionPricingItem Item { get; }
        public decimal LineSubtotal { get; }
        public decimal DiscountAmount { get; set; }
        public List<PromotionAppliedAdjustment> Adjustments { get; } = [];
    }
}
