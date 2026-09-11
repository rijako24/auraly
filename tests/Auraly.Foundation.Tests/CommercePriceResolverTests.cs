using Auraly.Domain.Pricing;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Pricing;
using Auraly.Platform.Domain.Promotions;

namespace Auraly.Foundation.Tests;

public sealed class CommercePriceResolverTests
{
    private static readonly Guid ChannelId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SoapId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid MeatId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SoapCategoryId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid MeatCategoryId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    [Fact]
    public void Anonymous_sale_without_promotions_keeps_public_prices()
    {
        var result = Resolve([Line("soap", SoapId, SoapCategoryId, 100m, 2m)]);

        AssertLine(result, "soap", 100m, "Base", null);
        Assert.Equal(200m, result.Total);
    }

    [Fact]
    public void Selecting_customer_reprices_the_same_document_with_its_channel()
    {
        var lines = new[] { Line("soap", SoapId, SoapCategoryId, 100m, 2m) };

        AssertLine(Resolve(lines), "soap", 100m, "Base", null);
        AssertLine(Resolve(lines, ChannelPolicy(
            tiers: [new(ChannelId, SoapId, 1m, 80m, "COP")])),
            "soap", 80m, "PriceChannel", ChannelId);
    }

    [Fact]
    public void Quantity_tier_uses_aggregate_quantity_and_updates_every_split_line()
    {
        var policy = ChannelPolicy(tiers:
        [
            new(ChannelId, SoapId, 1m, 90m, "COP"),
            new(ChannelId, SoapId, 3m, 75m, "COP")
        ]);

        var result = Resolve(
            [
                Line("first", SoapId, SoapCategoryId, 100m, 1m),
                Line("second", SoapId, SoapCategoryId, 100m, 2m)
            ], policy);

        AssertLine(result, "first", 75m, "PriceChannel", ChannelId);
        AssertLine(result, "second", 75m, "PriceChannel", ChannelId);
        Assert.Equal(225m, result.Total);
    }

    [Fact]
    public void Product_category_ancestor_and_brand_exclusions_preserve_public_price()
    {
        var ancestorId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var lines = new[]
        {
            Line("soap", SoapId, SoapCategoryId, 100m, productBrandId: brandId,
                categoryAncestors: [SoapCategoryId, ancestorId])
        };
        var tier = new PriceChannelTierRule(ChannelId, SoapId, 1m, 70m, "COP");

        foreach (var exclusion in new[]
        {
            new PriceChannelExclusionRule(ChannelId, SoapId, null, null),
            new PriceChannelExclusionRule(ChannelId, null, ancestorId, null),
            new PriceChannelExclusionRule(ChannelId, null, null, brandId)
        })
            AssertLine(Resolve(lines, ChannelPolicy([tier], [exclusion])),
                "soap", 100m, "Base", null);
    }

    [Theory]
    [InlineData(false, 90, "Promotion")]
    [InlineData(true, 72, "Promotion+PriceChannel")]
    public void Tenant_policy_controls_channel_and_promotion_combination(
        bool allowCombination, decimal expected, string source)
    {
        var promotion = PercentagePromotion(
            "Soap 10", PromotionItemType.Product, SoapId, null, 10m);
        var policy = ChannelPolicy(
            [new(ChannelId, SoapId, 1m, 80m, "COP")],
            allowCombination: allowCombination,
            promotions: [promotion]);

        AssertLine(Resolve([Line("soap", SoapId, SoapCategoryId, 100m)], policy),
            "soap", expected, source, source.Contains("PriceChannel") ? ChannelId : null);
    }

    [Fact]
    public void Cross_product_and_order_subtotal_promotions_use_the_whole_document()
    {
        var crossProduct = new PromotionRule(
            Guid.NewGuid(), "Soap unlocks meat", 20, true, null, DateTime.UnixEpoch,
            [new(PromotionItemType.Product, SoapId, null, 1m, null)],
            [new(PromotionBenefitType.PercentageDiscount, PromotionItemType.Product,
                MeatId, null, 50m, null, null, 1m)]);
        var orderThreshold = new PromotionRule(
            Guid.NewGuid(), "Order 10", 10, true, null, DateTime.UnixEpoch,
            [new(PromotionItemType.AnyProduct, null, null, 1m, 300m)],
            [new(PromotionBenefitType.PercentageDiscount, PromotionItemType.AnyProduct,
                null, null, 10m, null, null, null)]);
        var lines = new[]
        {
            Line("soap", SoapId, SoapCategoryId, 100m),
            Line("meat", MeatId, MeatCategoryId, 200m)
        };
        var policy = BasePolicy(promotions: [crossProduct, orderThreshold]);

        var document = Resolve(lines, policy);
        var catalogPreview = CommercePriceResolver.Resolve(lines, policy, independentLines: true);

        Assert.Equal(180m, document.Total);
        Assert.Equal(300m, catalogPreview.Total);
        AssertLine(catalogPreview, "soap", 100m, "Base", null);
        AssertLine(catalogPreview, "meat", 200m, "Base", null);
    }

    [Fact]
    public void Promotion_eligibility_does_not_overwrite_a_manual_line()
    {
        var promotion = PercentagePromotion(
            "Everything 20", PromotionItemType.AnyProduct, null, null, 20m);
        var result = Resolve(
            [
                Line("manual", SoapId, SoapCategoryId, 77m, eligibleForPromotion: false),
                Line("normal", MeatId, MeatCategoryId, 100m)
            ],
            BasePolicy(promotions: [promotion]));

        AssertLine(result, "manual", 77m, "Base", null);
        AssertLine(result, "normal", 80m, "Promotion", null);
    }

    [Fact]
    public void Maximum_document_size_resolves_deterministically_without_expanding_database_scope()
    {
        var lines = Enumerable.Range(1, 500)
            .Select(index => Line($"line-{index:D3}",
                Guid.Parse($"40000000-0000-0000-0000-{index:D12}"),
                index % 2 == 0 ? SoapCategoryId : MeatCategoryId,
                100m + index, 1m))
            .ToArray();
        var promotion = PercentagePromotion(
            "Everything 5", PromotionItemType.AnyProduct, null, null, 5m);

        var first = Resolve(lines, BasePolicy(promotions: [promotion]));
        var second = Resolve(lines.Reverse().ToArray(), BasePolicy(promotions: [promotion]));

        Assert.Equal(500, first.Lines.Count);
        Assert.Equal(first.Total, second.Total);
        Assert.Equal(first.DiscountTotal, second.DiscountTotal);
    }

    private static PromotionPriceResult Resolve(
        IReadOnlyList<CommercePriceLineInput> lines,
        CommercePricePolicy? policy = null) =>
        CommercePriceResolver.Resolve(lines, policy ?? BasePolicy());

    private static CommercePricePolicy BasePolicy(
        bool allowCombination = false,
        IReadOnlyList<PromotionRule>? promotions = null) =>
        new(null, [], [], [], allowCombination, promotions ?? []);

    private static CommercePricePolicy ChannelPolicy(
        IReadOnlyCollection<PriceChannelTierRule>? tiers = null,
        IReadOnlyCollection<PriceChannelExclusionRule>? exclusions = null,
        bool allowCombination = false,
        IReadOnlyList<PromotionRule>? promotions = null) =>
        new(ChannelId,
            [new(ChannelId, "TieredProductPrice", null)],
            tiers ?? [], exclusions ?? [], allowCombination, promotions ?? []);

    private static CommercePriceLineInput Line(
        string key, Guid productId, Guid categoryId, decimal price,
        decimal quantity = 1m, Guid? productBrandId = null,
        IReadOnlyCollection<Guid>? categoryAncestors = null,
        bool eligibleForPromotion = true) =>
        new(key, key, price, quantity,
            new(productId, categoryId, productBrandId,
                categoryAncestors ?? [categoryId], "COP", 50m, 50m, null),
            EligibleForPromotion: eligibleForPromotion);

    private static PromotionRule PercentagePromotion(
        string name, PromotionItemType targetType, Guid? productId,
        Guid? categoryId, decimal percentage) =>
        new(Guid.NewGuid(), name, 10, true, null, DateTime.UnixEpoch, [],
            [new(PromotionBenefitType.PercentageDiscount, targetType,
                productId, null, percentage, null, null, null, categoryId)]);

    private static void AssertLine(
        PromotionPriceResult result, string key, decimal expectedUnitPrice,
        string source, Guid? channelId)
    {
        var line = result.Lines.Single(item => item.Input.Key == key);
        Assert.Equal(expectedUnitPrice, line.EffectiveUnitPrice, 6);
        Assert.Equal(source, line.PriceSource);
        Assert.Equal(channelId, line.PriceChannelId);
    }
}
