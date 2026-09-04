using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Promotions;

public sealed record PromotionPriceLineInput(
    string Key,
    PromotionItemType ItemType,
    Guid? ProductId,
    Guid? ServiceId,
    string Name,
    string? CategoryName,
    decimal BaseUnitPrice,
    decimal? ChannelUnitPrice,
    decimal Quantity,
    string CurrencyCode,
    Guid? PriceChannelId = null,
    bool IncludeInTotal = true,
    bool EligibleForPromotion = true);

public sealed record PromotionConditionRule(
    PromotionItemType ItemType,
    Guid? ProductId,
    Guid? ServiceId,
    string? CategoryName,
    decimal MinQuantity,
    decimal? MinSubtotal);

public sealed record PromotionBenefitRule(
    PromotionBenefitType BenefitType,
    PromotionItemType TargetItemType,
    Guid? ProductId,
    Guid? ServiceId,
    string? CategoryName,
    decimal? DiscountPercentage,
    decimal? DiscountAmount,
    decimal? FixedUnitPrice,
    decimal? AppliesToQuantity);

public sealed record PromotionRule(
    Guid PromotionId,
    string Name,
    int Priority,
    bool IsCombinable,
    string? CouponCode,
    DateTime CreatedAtUtc,
    IReadOnlyList<PromotionConditionRule> Conditions,
    IReadOnlyList<PromotionBenefitRule> Benefits);

public sealed record PromotionPriceAdjustment(
    Guid PromotionId,
    string PromotionName,
    decimal DiscountAmount,
    string Summary);

public sealed record PromotionPriceLineResult(
    PromotionPriceLineInput Input,
    decimal ReferenceUnitPrice,
    decimal EffectiveUnitPrice,
    decimal DiscountAmount,
    decimal LineTotal,
    string PriceSource,
    Guid? PriceChannelId,
    IReadOnlyList<PromotionPriceAdjustment> Adjustments);

public sealed record PromotionPriceResult(
    IReadOnlyList<PromotionPriceLineResult> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Total);

/// <summary>
/// Canonical, storage-independent promotion and channel composition policy.
/// SQL Server and POS Edge must supply equivalent inputs and call this resolver.
/// </summary>
public static class PromotionPriceResolver
{
    public static PromotionPriceResult Resolve(
        IReadOnlyList<PromotionPriceLineInput> lines,
        IReadOnlyList<PromotionRule> promotions,
        bool allowPromotionChannelCombination,
        string? couponCode = null)
    {
        if (lines.Count == 0)
            return new([], 0, 0, 0);
        if (lines.Any(line => line.Quantity <= 0 || line.BaseUnitPrice < 0 || line.ChannelUnitPrice < 0))
            throw new ArgumentOutOfRangeException(nameof(lines), "Prices must be non-negative and quantities positive.");

        var ordered = promotions
            .Where(rule => CouponMatches(rule.CouponCode, couponCode))
            .Where(rule => rule.Conditions.Count == 0 || rule.Conditions.All(condition => MatchesCondition(condition, lines)))
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.CreatedAtUtc)
            .ThenBy(rule => rule.PromotionId)
            .ToArray();
        var states = lines.ToDictionary(line => line.Key, line => new MutableLine(line), StringComparer.OrdinalIgnoreCase);
        var rulesById = ordered.ToDictionary(rule => rule.PromotionId);

        foreach (var promotion in ordered)
        foreach (var benefit in promotion.Benefits)
        {
            var targets = states.Values
                .Where(value => value.Input.EligibleForPromotion && MatchesTarget(
                    benefit.TargetItemType, benefit.ProductId, benefit.ServiceId,
                    benefit.CategoryName, value.Input))
                .Where(state => !state.AppliedPromotionIds
                    .Where(id => id != promotion.PromotionId)
                    .Select(id => rulesById[id])
                    .Any(applied => !applied.IsCombinable || !promotion.IsCombinable))
                .OrderBy(state => state.Input.ItemType)
                .ThenBy(state => state.Input.ProductId)
                .ThenBy(state => state.Input.ServiceId)
                .ThenBy(state => state.Input.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var remainingQuantity = benefit.AppliesToQuantity;
            var remainingAmount = benefit.BenefitType == PromotionBenefitType.AmountDiscount
                ? benefit.DiscountAmount ?? 0
                : (decimal?)null;
            var benefitApplied = false;
            foreach (var state in targets)
            {
                if (remainingQuantity is <= 0 || remainingAmount is <= 0)
                    break;
                var eligibleQuantity = remainingQuantity.HasValue
                    ? Math.Min(state.Input.Quantity, remainingQuantity.Value)
                    : state.Input.Quantity;
                var basis = state.PromotionBasis(allowPromotionChannelCombination);
                var discount = CalculateDiscount(
                    benefit, state, basis, eligibleQuantity, remainingAmount);
                if (discount <= 0)
                    continue;

                state.Apply(promotion, benefit, basis, discount);
                benefitApplied = true;
                if (remainingQuantity.HasValue)
                    remainingQuantity -= eligibleQuantity;
                if (remainingAmount.HasValue)
                    remainingAmount -= discount;
            }
            if (benefitApplied)
                foreach (var target in targets)
                    target.RegisterPromotionScope(promotion.PromotionId);
            if (benefitApplied && !allowPromotionChannelCombination)
                foreach (var target in targets)
                    target.UsePublicBasis();
        }

        var results = states.Values.Select(state => state.ToResult()).ToArray();
        var subtotal = results.Where(line => line.Input.IncludeInTotal)
            .Sum(line => line.ReferenceUnitPrice * line.Input.Quantity);
        var discountTotal = results.Where(line => line.Input.IncludeInTotal).Sum(line => line.DiscountAmount);
        return new(results, subtotal, discountTotal, subtotal - discountTotal);
    }

    private static bool CouponMatches(string? required, string? supplied) =>
        string.IsNullOrWhiteSpace(required)
        || string.Equals(required.Trim(), supplied?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesCondition(
        PromotionConditionRule condition,
        IReadOnlyList<PromotionPriceLineInput> lines)
    {
        var matches = lines.Where(line => MatchesTarget(
            condition.ItemType, condition.ProductId, condition.ServiceId, condition.CategoryName, line)).ToArray();
        return matches.Sum(line => line.Quantity) >= condition.MinQuantity
            && (condition.MinSubtotal is null
                || matches.Sum(line => line.BaseUnitPrice * line.Quantity) >= condition.MinSubtotal.Value);
    }

    private static bool MatchesTarget(
        PromotionItemType targetType,
        Guid? productId,
        Guid? serviceId,
        string? categoryName,
        PromotionPriceLineInput line) => targetType switch
        {
            PromotionItemType.Any => true,
            PromotionItemType.AnyProduct => line.ItemType == PromotionItemType.Product,
            PromotionItemType.AnyService => line.ItemType == PromotionItemType.Service,
            PromotionItemType.Product => line.ItemType == PromotionItemType.Product && productId == line.ProductId,
            PromotionItemType.Service => line.ItemType == PromotionItemType.Service && serviceId == line.ServiceId,
            PromotionItemType.ProductCategory => line.ItemType == PromotionItemType.Product && Same(categoryName, line.CategoryName),
            PromotionItemType.ServiceCategory => line.ItemType == PromotionItemType.Service && Same(categoryName, line.CategoryName),
            _ => false
        };

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static decimal CalculateDiscount(
        PromotionBenefitRule benefit,
        MutableLine line,
        decimal referenceUnitPrice,
        decimal quantity,
        decimal? remainingAmount)
    {
        var remaining = Math.Max(0, referenceUnitPrice * line.Input.Quantity - line.DiscountAmount);
        if (remaining <= 0)
            return 0;
        if (quantity <= 0)
            return 0;
        var eligibleSubtotal = Math.Min(remaining, referenceUnitPrice * quantity);
        return benefit.BenefitType switch
        {
            PromotionBenefitType.PercentageDiscount =>
                eligibleSubtotal * Math.Clamp(benefit.DiscountPercentage ?? 0, 0, 100) / 100,
            PromotionBenefitType.AmountDiscount => Math.Min(eligibleSubtotal, remainingAmount ?? 0),
            PromotionBenefitType.FixedUnitPrice => Math.Min(eligibleSubtotal,
                Math.Max(0, referenceUnitPrice - (benefit.FixedUnitPrice ?? referenceUnitPrice)) * quantity),
            PromotionBenefitType.FreeItem => eligibleSubtotal,
            _ => 0
        };
    }

    private static string BuildSummary(PromotionRule promotion, PromotionBenefitRule benefit) =>
        benefit.BenefitType switch
        {
            PromotionBenefitType.PercentageDiscount => $"{promotion.Name}: {benefit.DiscountPercentage:0.##}% de descuento",
            PromotionBenefitType.AmountDiscount => $"{promotion.Name}: descuento de {benefit.DiscountAmount:0.##}",
            PromotionBenefitType.FixedUnitPrice => $"{promotion.Name}: precio promocional {benefit.FixedUnitPrice:0.##}",
            PromotionBenefitType.FreeItem => $"{promotion.Name}: item gratis",
            _ => promotion.Name
        };

    private sealed class MutableLine
    {
        public MutableLine(PromotionPriceLineInput input)
        {
            Input = input;
            ReferenceUnitPrice = input.ChannelUnitPrice ?? input.BaseUnitPrice;
        }

        public PromotionPriceLineInput Input { get; }
        public decimal ReferenceUnitPrice { get; private set; }
        public decimal DiscountAmount { get; set; }
        public HashSet<Guid> AppliedPromotionIds { get; } = [];
        public List<PromotionPriceAdjustment> Adjustments { get; } = [];

        public decimal PromotionBasis(bool allowChannelCombination) =>
            Adjustments.Count > 0
                ? ReferenceUnitPrice
                : allowChannelCombination
                ? Input.ChannelUnitPrice ?? Input.BaseUnitPrice
                : Input.BaseUnitPrice;

        public void Apply(
            PromotionRule promotion,
            PromotionBenefitRule benefit,
            decimal referenceUnitPrice,
            decimal discount)
        {
            ReferenceUnitPrice = referenceUnitPrice;
            DiscountAmount += discount;
            AppliedPromotionIds.Add(promotion.PromotionId);
            Adjustments.Add(new(
                promotion.PromotionId,
                promotion.Name,
                discount,
                BuildSummary(promotion, benefit)));
        }

        public void UsePublicBasis() => ReferenceUnitPrice = Input.BaseUnitPrice;

        public void RegisterPromotionScope(Guid promotionId) => AppliedPromotionIds.Add(promotionId);

        public PromotionPriceLineResult ToResult()
        {
            var subtotal = ReferenceUnitPrice * Input.Quantity;
            var discount = Math.Min(DiscountAmount, subtotal);
            var total = subtotal - discount;
            var effective = total / Input.Quantity;
            var source = Adjustments.Count > 0
                ? Input.ChannelUnitPrice is not null && ReferenceUnitPrice == Input.ChannelUnitPrice
                    ? "Promotion+PriceChannel"
                    : "Promotion"
                : Input.ChannelUnitPrice is not null && ReferenceUnitPrice == Input.ChannelUnitPrice
                    ? "PriceChannel"
                    : "Base";
            return new(Input, ReferenceUnitPrice, effective, discount, total, source,
                source.Contains("PriceChannel", StringComparison.Ordinal) ? Input.PriceChannelId : null,
                Adjustments);
        }
    }
}
