using Auraly.Domain.Pricing;

namespace Auraly.Foundation.Tests;

public sealed class PriceChannelResolverTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void Percentage_rule_is_calculated_from_public_price()
    {
        var result = Resolve("PercentageOverBasePrice", -10m);

        Assert.Equal(135m,result.Amount);
        Assert.Equal(ChannelId,result.PriceChannelId);
    }

    [Fact]
    public void Quantity_rule_selects_the_highest_configured_applicable_tier()
    {
        var product = Product();
        var result = PriceChannelResolver.Resolve(ChannelId,100m,7m,product,
            [new(ChannelId,"TieredProductPrice",null)],
            [new(ChannelId,ProductId,1m,145m,"COP"),new(ChannelId,ProductId,5m,120m,"COP"),new(ChannelId,ProductId,10m,110m,"COP")],
            []);

        Assert.Equal(120m,result.Amount);
    }

    [Fact]
    public void Cost_and_margin_strategies_use_the_product_catalog_inputs()
    {
        Assert.Equal(137.5m,Resolve("MarginOverLatestCost",20m).Amount);
        Assert.Equal(125m,Resolve("FixedMarginOverAverageCost",20m).Amount);
        Assert.Equal(100m,Resolve("SellAtAverageCost",null).Amount);
        Assert.Equal(171.4286m,Resolve("ProductMarginAdjustment",10m).Amount);
    }

    [Fact]
    public void Average_cost_is_the_floor_for_percentage_and_tier_rules()
    {
        Assert.Equal(100m,Resolve("PercentageOverBasePrice",-50m).Amount);
        var result = PriceChannelResolver.Resolve(ChannelId,150m,1m,Product(),
            [new(ChannelId,"TieredProductPrice",null)],
            [new(ChannelId,ProductId,1m,80m,"COP")],[]);
        Assert.Equal(100m,result.Amount);
    }

    [Fact]
    public void Product_brand_and_category_ancestor_exclusions_prevent_the_channel()
    {
        var categoryId = Guid.NewGuid();
        var ancestorId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var product = Product(categoryId,brandId,[categoryId,ancestorId]);

        foreach (var exclusion in new[]
        {
            new PriceChannelExclusionRule(ChannelId,ProductId,null,null),
            new PriceChannelExclusionRule(ChannelId,null,null,brandId),
            new PriceChannelExclusionRule(ChannelId,null,ancestorId,null)
        })
        {
            var result = PriceChannelResolver.Resolve(ChannelId,150m,1m,product,
                [new(ChannelId,"PercentageOverBasePrice",-10m)],[],[exclusion]);
            Assert.False(result.Applied);
        }
    }

    [Fact]
    public void Missing_channel_or_missing_quantity_tier_falls_back_without_guessing()
    {
        Assert.False(PriceChannelResolver.Resolve(Guid.NewGuid(),150m,1m,Product(),[],[],[]).Applied);
        Assert.False(PriceChannelResolver.Resolve(ChannelId,150m,1m,Product(),
            [new(ChannelId,"TieredProductPrice",null)],[],[]).Applied);
        Assert.False(PriceChannelResolver.Resolve(ChannelId,150m,1m,Product(),
            [new(ChannelId,"TieredProductPrice",null)],
            [new(ChannelId,ProductId,1m,80m,"USD")],[]).Applied);
    }

    private static PriceChannelResolution Resolve(string strategy,decimal? value) =>
        PriceChannelResolver.Resolve(ChannelId,150m,1m,Product(),
            [new(ChannelId,strategy,value)],[],[]);

    private static PriceChannelProductContext Product(
        Guid? categoryId=null,Guid? brandId=null,IReadOnlyCollection<Guid>? ancestors=null) =>
        new(ProductId,categoryId,brandId,ancestors ?? [],"COP",100m,110m,20m);
}
