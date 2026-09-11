using Auraly.Domain.Pricing;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Promotions;

namespace Auraly.Platform.Domain.Pricing;

public sealed record CommercePriceLineInput(
    string Key,
    string Name,
    decimal BaseUnitPrice,
    decimal Quantity,
    PriceChannelProductContext Product,
    bool IncludeInTotal = true,
    bool EligibleForPromotion = true);

public sealed record CommercePricePolicy(
    Guid? PriceChannelId,
    IReadOnlyCollection<PriceChannelRule> PriceChannels,
    IReadOnlyCollection<PriceChannelTierRule> PriceChannelTiers,
    IReadOnlyCollection<PriceChannelExclusionRule> PriceChannelExclusions,
    bool AllowPromotionChannelCombination,
    IReadOnlyList<PromotionRule> Promotions);

/// <summary>
/// Canonical coordinator for product price-channel and promotion resolution.
/// Storage adapters load the policy and the requested document lines; this type owns
/// aggregate-quantity handling and composition of the two canonical calculators.
/// </summary>
public static class CommercePriceResolver
{
    public static PromotionPriceResult Resolve(
        IReadOnlyList<CommercePriceLineInput> lines,
        CommercePricePolicy policy,
        bool independentLines = false)
    {
        if (lines.Count == 0)
            return new([], 0, 0, 0);

        var quantities = lines
            .GroupBy(line => line.Product.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        var promotionInputs = lines.Select(line =>
        {
            var channel = PriceChannelResolver.Resolve(
                policy.PriceChannelId,
                line.BaseUnitPrice,
                quantities[line.Product.ProductId],
                line.Product,
                policy.PriceChannels,
                policy.PriceChannelTiers,
                policy.PriceChannelExclusions);
            return new PromotionPriceLineInput(
                line.Key,
                PromotionItemType.Product,
                line.Product.ProductId,
                null,
                line.Name,
                line.BaseUnitPrice,
                channel.Amount,
                line.Quantity,
                line.Product.CurrencyCode,
                channel.PriceChannelId,
                line.IncludeInTotal,
                line.EligibleForPromotion,
                line.Product.ProductCategoryId);
        }).ToArray();

        if (independentLines)
        {
            var resolved = promotionInputs.SelectMany(input => PromotionPriceResolver.Resolve(
                [input], policy.Promotions, policy.AllowPromotionChannelCombination).Lines).ToArray();
            return Summarize(resolved);
        }

        return PromotionPriceResolver.Resolve(
            promotionInputs, policy.Promotions, policy.AllowPromotionChannelCombination);
    }

    private static PromotionPriceResult Summarize(IReadOnlyList<PromotionPriceLineResult> lines)
    {
        var included = lines.Where(line => line.Input.IncludeInTotal).ToArray();
        var subtotal = included.Sum(line => line.ReferenceUnitPrice * line.Input.Quantity);
        var discount = included.Sum(line => line.DiscountAmount);
        return new(lines, subtotal, discount, subtotal - discount);
    }
}
