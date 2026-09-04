using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Promotions;

namespace Auraly.Foundation.Tests;

public sealed class PromotionPriceResolverTests
{
    private static readonly Guid Cleaning = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Meat = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void No_promotion_or_channel_returns_public_price()
    {
        var result = Resolve([Line("p", Cleaning, 100)]);
        AssertLine(result, "p", 100, "Base", 0);
    }

    [Fact]
    public void Channel_without_promotion_returns_channel_price()
    {
        var result = Resolve([Line("p", Cleaning, 100, 80)]);
        AssertLine(result, "p", 80, "PriceChannel", 0);
    }

    [Fact]
    public void Promotion_wins_over_channel_when_tenant_disallows_combination()
    {
        var result = Resolve([Line("p", Cleaning, 100, 80)], [Percent("clean", "Aseo", 10)], false);
        AssertLine(result, "p", 90, "Promotion", 10);
    }

    [Fact]
    public void Promotion_is_applied_to_channel_when_tenant_allows_combination()
    {
        var result = Resolve([Line("p", Cleaning, 100, 80)], [Percent("clean", "Aseo", 10)], true);
        AssertLine(result, "p", 72, "Promotion+PriceChannel", 8);
    }

    [Fact]
    public void Non_overlapping_non_combinable_promotions_both_apply()
    {
        var result = Resolve(
            [Line("soap", Cleaning, 100), Line("ham", Meat, 200)],
            [Percent("clean", "Aseo", 10, combinable: false), Percent("meat", "Carnes", 20, combinable: false)]);
        AssertLine(result, "soap", 90, "Promotion", 10);
        AssertLine(result, "ham", 160, "Promotion", 40);
    }

    [Fact]
    public void Higher_priority_non_combinable_promotion_wins_only_on_overlapping_line()
    {
        var global = Percent("global", null, 10, priority: 20, combinable: false);
        var cleaning = Percent("clean", "Aseo", 50, priority: 10, combinable: true);
        var result = Resolve(
            [Line("soap", Cleaning, 100), Line("ham", Meat, 100)],
            [cleaning, global]);
        AssertLine(result, "soap", 90, "Promotion", 10);
        AssertLine(result, "ham", 90, "Promotion", 10);
    }

    [Fact]
    public void Two_combinable_promotions_stack_sequentially_on_same_line()
    {
        var result = Resolve(
            [Line("soap", Cleaning, 100)],
            [Percent("global", null, 10, 20, true), Percent("clean", "Aseo", 10, 10, true)]);
        AssertLine(result, "soap", 81, "Promotion", 19);
        Assert.Equal(2, result.Lines.Single().Adjustments.Count);
    }

    [Fact]
    public void Minimum_cart_subtotal_applies_purchase_discount_to_all_products()
    {
        var rule = new PromotionRule(
            Guid.NewGuid(), "Compra mayor", 0, false, null, DateTime.UnixEpoch,
            [new(PromotionItemType.AnyProduct, null, null, null, 1, 300)],
            [new(PromotionBenefitType.PercentageDiscount, PromotionItemType.AnyProduct,
                null, null, null, 10, null, null, null)]);
        var result = Resolve([Line("one", Cleaning, 100), Line("two", Meat, 200)], [rule]);
        Assert.Equal(270, result.Total);
        Assert.Equal(30, result.DiscountTotal);
    }

    [Fact]
    public void Buy_three_get_one_free_discounts_one_unit()
    {
        var productId = Guid.NewGuid();
        var rule = new PromotionRule(
            Guid.NewGuid(), "Tres por dos", 0, false, null, DateTime.UnixEpoch,
            [new(PromotionItemType.Product, productId, null, null, 3, null)],
            [new(PromotionBenefitType.FreeItem, PromotionItemType.Product,
                productId, null, null, null, null, null, 1)]);
        var result = Resolve([Line("item", Cleaning, 50, quantity: 3, productId: productId)], [rule]);
        AssertLine(result, "item", 100m / 3m, "Promotion", 50);
        Assert.Equal(100, result.Total);
    }

    [Fact]
    public void Quantity_limited_benefit_is_not_multiplied_by_split_lines()
    {
        var productId = Guid.NewGuid();
        var rule = new PromotionRule(
            Guid.NewGuid(), "Tres por dos", 0, false, null, DateTime.UnixEpoch,
            [new(PromotionItemType.Product, productId, null, null, 3, null)],
            [new(PromotionBenefitType.FreeItem, PromotionItemType.Product,
                productId, null, null, null, null, null, 1)]);
        var result = Resolve(
            [
                Line("one", Cleaning, 50, quantity: 1, productId: productId),
                Line("two", Cleaning, 50, quantity: 1, productId: productId),
                Line("three", Cleaning, 50, quantity: 1, productId: productId)
            ],
            [rule]);
        Assert.Equal(50, result.DiscountTotal);
        Assert.Equal(100, result.Total);
        Assert.Single(result.Lines.Where(line => line.DiscountAmount > 0));
    }

    [Fact]
    public void Non_combinable_quantity_limited_promotion_blocks_overlap_independently_of_line_splitting()
    {
        var productId = Guid.NewGuid();
        var free = new PromotionRule(
            Guid.NewGuid(), "Tres por dos", 20, false, null, DateTime.UnixEpoch,
            [new(PromotionItemType.Product, productId, null, null, 3, null)],
            [new(PromotionBenefitType.FreeItem, PromotionItemType.Product,
                productId, null, null, null, null, null, 1)]);
        var percentage = Percent("Diez por ciento", null, 10, priority: 10, combinable: true);

        var grouped = Resolve(
            [Line("grouped", Cleaning, 50, quantity: 3, productId: productId)],
            [percentage, free]);
        var split = Resolve(
            [
                Line("one", Cleaning, 50, productId: productId),
                Line("two", Cleaning, 50, productId: productId),
                Line("three", Cleaning, 50, productId: productId)
            ],
            [percentage, free]);

        Assert.Equal(100, grouped.Total);
        Assert.Equal(grouped.Total, split.Total);
        Assert.All(split.Lines.SelectMany(line => line.Adjustments), adjustment =>
            Assert.Equal(free.PromotionId, adjustment.PromotionId));
    }

    [Fact]
    public void Fixed_amount_benefit_is_allocated_once_across_matching_lines()
    {
        var rule = new PromotionRule(
            Guid.NewGuid(), "Descuento compra", 0, true, null, DateTime.UnixEpoch, [],
            [new(PromotionBenefitType.AmountDiscount, PromotionItemType.AnyProduct,
                null, null, null, null, 30, null, null)]);
        var result = Resolve(
            [Line("one", Cleaning, 100), Line("two", Meat, 100)], [rule]);
        Assert.Equal(30, result.DiscountTotal);
        Assert.Equal(170, result.Total);
    }

    [Fact]
    public void Fixed_unit_price_uses_channel_as_basis_only_when_tenant_allows_combination()
    {
        var rule = new PromotionRule(
            Guid.NewGuid(), "Precio fijo", 0, false, null, DateTime.UnixEpoch, [],
            [new(PromotionBenefitType.FixedUnitPrice, PromotionItemType.AnyProduct,
                null, null, null, null, null, 60, null)]);
        var line = Line("item", Cleaning, 100, channel: 80);

        AssertLine(Resolve([line], [rule], combineChannel: false), "item", 60, "Promotion", 40);
        AssertLine(Resolve([line], [rule], combineChannel: true), "item", 60, "Promotion+PriceChannel", 20);
    }

    [Fact]
    public void Coupon_promotion_does_not_apply_without_matching_coupon()
    {
        var promotion = Percent("coupon", null, 10) with { CouponCode = "SAVE10" };
        AssertLine(Resolve([Line("p", Cleaning, 100)], [promotion]), "p", 100, "Base", 0);
        AssertLine(Resolve([Line("p", Cleaning, 100)], [promotion], coupon: "save10"), "p", 90, "Promotion", 10);
    }

    [Fact]
    public void Priority_and_tie_breakers_are_deterministic()
    {
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var first = Percent("first", null, 10, priority: 5) with { PromotionId = firstId };
        var second = Percent("second", null, 50, priority: 5) with { PromotionId = secondId };
        var result = Resolve([Line("p", Cleaning, 100)], [second, first]);
        AssertLine(result, "p", 90, "Promotion", 10);
        Assert.Equal(firstId, result.Lines.Single().Adjustments.Single().PromotionId);
    }

    private static PromotionPriceResult Resolve(
        IReadOnlyList<PromotionPriceLineInput> lines,
        IReadOnlyList<PromotionRule>? rules = null,
        bool combineChannel = false,
        string? coupon = null) =>
        PromotionPriceResolver.Resolve(lines, rules ?? [], combineChannel, coupon);

    private static PromotionPriceLineInput Line(
        string key, Guid categoryMarker, decimal price, decimal? channel = null,
        decimal quantity = 1, Guid? productId = null) => new(
        key, PromotionItemType.Product, productId ?? Guid.NewGuid(), null, key,
        categoryMarker == Cleaning ? "Aseo" : "Carnes", price, channel, quantity,
        "COP", channel is null ? null : Guid.NewGuid());

    private static PromotionRule Percent(
        string name, string? category, decimal percent, int priority = 0, bool combinable = false) => new(
        Guid.NewGuid(), name, priority, combinable, null, DateTime.UnixEpoch,
        [],
        [new(PromotionBenefitType.PercentageDiscount,
            category is null ? PromotionItemType.AnyProduct : PromotionItemType.ProductCategory,
            null, null, category, percent, null, null, null)]);

    private static void AssertLine(
        PromotionPriceResult result, string key, decimal expectedUnitPrice,
        string source, decimal discount)
    {
        var line = result.Lines.Single(value => value.Input.Key == key);
        Assert.Equal(expectedUnitPrice, line.EffectiveUnitPrice, 6);
        Assert.Equal(source, line.PriceSource);
        Assert.Equal(discount, line.DiscountAmount, 6);
    }
}
