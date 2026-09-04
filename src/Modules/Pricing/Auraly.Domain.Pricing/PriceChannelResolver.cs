namespace Auraly.Domain.Pricing;

public sealed record PriceChannelRule(
    Guid PriceChannelId,
    string Strategy,
    decimal? Value);

public sealed record PriceChannelTierRule(
    Guid PriceChannelId,
    Guid ProductId,
    decimal MinimumQuantity,
    decimal Amount,
    string CurrencyCode);

public sealed record PriceChannelExclusionRule(
    Guid PriceChannelId,
    Guid? ProductId,
    Guid? ProductCategoryId,
    Guid? ProductBrandId);

public sealed record PriceChannelProductContext(
    Guid ProductId,
    Guid? ProductCategoryId,
    Guid? ProductBrandId,
    IReadOnlyCollection<Guid> ProductCategoryAncestorIds,
    string CurrencyCode,
    decimal AverageCost,
    decimal LatestCost,
    decimal? TargetMarginPercent);

public sealed record PriceChannelResolution(decimal? Amount, Guid? PriceChannelId)
{
    public bool Applied => Amount.HasValue && PriceChannelId.HasValue;
}

/// <summary>
/// Canonical, deterministic channel calculator shared by server and POS Edge.
/// Adapters only load rules and product inputs; no adapter owns pricing arithmetic.
/// </summary>
public static class PriceChannelResolver
{
    public static PriceChannelResolution Resolve(
        Guid? priceChannelId,
        decimal baseAmount,
        decimal quantity,
        PriceChannelProductContext product,
        IReadOnlyCollection<PriceChannelRule> channels,
        IReadOnlyCollection<PriceChannelTierRule> tiers,
        IReadOnlyCollection<PriceChannelExclusionRule> exclusions)
    {
        if (priceChannelId is null || quantity <= 0 || baseAmount < 0)
            return new(null, null);

        var channel = channels.SingleOrDefault(value => value.PriceChannelId == priceChannelId.Value);
        if (channel is null || IsExcluded(channel.PriceChannelId, product, exclusions))
            return new(null, null);

        decimal? specialAmount = null;
        if (string.Equals(channel.Strategy, "TieredProductPrice", StringComparison.Ordinal))
            specialAmount = tiers
                .Where(value => value.PriceChannelId == channel.PriceChannelId
                    && value.ProductId == product.ProductId
                    && string.Equals(value.CurrencyCode, product.CurrencyCode, StringComparison.OrdinalIgnoreCase)
                    && value.MinimumQuantity <= quantity)
                .OrderByDescending(value => value.MinimumQuantity)
                .Select(value => (decimal?)value.Amount)
                .FirstOrDefault();

        var amount = CalculateAmount(
            channel.Strategy, channel.Value, baseAmount, product.AverageCost,
            product.LatestCost, product.TargetMarginPercent, specialAmount);
        return amount.HasValue
            ? new(amount, channel.PriceChannelId)
            : new(null, null);
    }

    public static decimal? CalculateAmount(
        string strategy,
        decimal? value,
        decimal baseAmount,
        decimal averageCost,
        decimal latestCost,
        decimal? productTargetMarginPercent,
        decimal? specialAmount)
    {
        decimal? calculated = strategy switch
        {
            "TieredProductPrice" => specialAmount,
            "PercentageOverBasePrice" => baseAmount * (1 + (value ?? 0) / 100),
            "MarginOverLatestCost" when latestCost > 0 =>
                latestCost / (1 - (value ?? 0) / 100),
            "FixedMarginOverAverageCost" when averageCost > 0 =>
                averageCost / (1 - (value ?? 0) / 100),
            "SellAtAverageCost" when averageCost > 0 => averageCost,
            "ProductMarginAdjustment" => CalculateAdjustedMargin(
                baseAmount, averageCost, productTargetMarginPercent, value ?? 0),
            _ => null
        };

        if (calculated is null || calculated < 0)
            return null;
        if (averageCost > 0 && calculated < averageCost)
            calculated = averageCost;
        return decimal.Round(calculated.Value, 4, MidpointRounding.AwayFromZero);
    }

    private static decimal? CalculateAdjustedMargin(
        decimal baseAmount,
        decimal averageCost,
        decimal? productTargetMarginPercent,
        decimal adjustment)
    {
        var targetMargin = productTargetMarginPercent
            ?? (baseAmount > 0 ? 100 - 100 * averageCost / baseAmount : null);
        if (targetMargin is null)
            return null;
        var adjustedMargin = Math.Clamp(targetMargin.Value + adjustment, 0, 99.999999m);
        if (baseAmount > 0 && targetMargin is >= 0 and < 100)
            return baseAmount * (1 - targetMargin.Value / 100) / (1 - adjustedMargin / 100);
        return averageCost > 0 ? averageCost / (1 - adjustedMargin / 100) : null;
    }

    private static bool IsExcluded(
        Guid channelId,
        PriceChannelProductContext product,
        IReadOnlyCollection<PriceChannelExclusionRule> exclusions) =>
        exclusions.Any(value => value.PriceChannelId == channelId &&
            (value.ProductId == product.ProductId
             || value.ProductBrandId.HasValue && value.ProductBrandId == product.ProductBrandId
             || value.ProductCategoryId.HasValue
                && (value.ProductCategoryId == product.ProductCategoryId
                    || product.ProductCategoryAncestorIds.Contains(value.ProductCategoryId.Value))));
}
