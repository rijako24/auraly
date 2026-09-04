using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Platform.Domain.Enums;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Foundation.Tests;

public sealed class PosPricingStoreTests
{
    [Fact]
    public async Task Local_resolver_uses_channel_quantity_tiers_and_always_falls_back_to_business_price()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-pricing-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PosCatalogStore($"Data Source={path}");
            await store.InitializeAsync();
            var productId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var item = new PosCatalogItem(
                productId, "P-1", "REF-1", "Product", "EA", "VAT19", 19m,
                100m, "COP", true, null, ["7701"], []);
            var items = new[] { item };
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items))))
                .ToLowerInvariant();
            await store.BeginBootstrapAsync(
                new CatalogSyncSessionResponse(sessionId, 0, 1, DateTimeOffset.UtcNow.AddHours(1)));
            await store.ApplyBootstrapPageAsync(
                new CatalogBootstrapPage(sessionId, 0, null, false, hash, items));
            await store.PromoteBootstrapAsync();

            var channelId = Guid.NewGuid();
            var channelCustomer = Guid.NewGuid();
            var excludedCustomer = Guid.NewGuid();
            await store.ApplyPricingSnapshotAsync(new PosPricingSnapshot(
                [new(channelId,"TIER","Tiered", "TieredProductPrice",null)],
                [
                    new(channelId, productId, 1m, 90m, "COP"),
                    new(channelId, productId, 5m, 80m, "COP")
                ],
                [],
                [
                    new(channelCustomer, "2", "Channel customer", channelId, true),
                    new(excludedCustomer, "3", "Missing channel item", Guid.NewGuid(), true)
                ]));

            var channelOne = await store.ResolvePriceAsync(productId, channelCustomer, 1m);
            Assert.Equal("PriceChannel", channelOne.Source);
            Assert.Equal(90m, channelOne.Amount);
            var channelFive = await store.ResolvePriceAsync(productId, channelCustomer, 5m);
            Assert.Equal(80m, channelFive.Amount);

            var missingSpecial = await store.ResolvePriceAsync(productId, excludedCustomer, 1m);
            Assert.Equal("Base", missingSpecial.Source);
            Assert.Equal(100m, missingSpecial.Amount);
            var anonymous = await store.ResolvePriceAsync(productId, null, 1m);
            Assert.Equal(100m, anonymous.Amount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Local_resolver_applies_category_threshold_and_buy_three_rules_with_channel_policy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-promotions-{Guid.NewGuid():N}.db");
        try
        {
            var now = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
            var clock = new FixedTimeProvider(now);
            var store = new PosCatalogStore($"Data Source={path}", clock);
            await store.InitializeAsync();
            var hygieneId = Guid.NewGuid();
            var meatId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var items = new[]
            {
                new PosCatalogItem(hygieneId, "A-1", null, "Soap", "EA", "01", 0m,
                    100m, "COP", IsActive: true, IsWeighable: false,
                    AllowsFractionalSale: false, Scale: null, Barcodes: [], Identifiers: [],
                    CategoryName: "Hygiene"),
                new PosCatalogItem(meatId, "M-1", null, "Meat", "EA", "01", 0m,
                    200m, "COP", IsActive: true, IsWeighable: false,
                    AllowsFractionalSale: false, Scale: null, Barcodes: [], Identifiers: [],
                    CategoryName: "Meat")
            };
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items))))
                .ToLowerInvariant();
            await store.BeginBootstrapAsync(
                new CatalogSyncSessionResponse(sessionId, 0, items.Length, now.AddHours(1)));
            await store.ApplyBootstrapPageAsync(
                new CatalogBootstrapPage(sessionId, 0, null, false, hash, items));
            await store.PromoteBootstrapAsync();

            var channelId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            PosPromotion CategoryPromotion(string name, string category, int priority) => new(
                Guid.NewGuid(), name, priority, false, null, now.AddDays(-1), now.AddDays(1), now,
                [new((int)PromotionItemType.ProductCategory, null, null, category, 1m, null)],
                [new((int)PromotionBenefitType.PercentageDiscount,
                    (int)PromotionItemType.ProductCategory, null, null, category, 10m, null, null, null)]);
            var categories = new[]
            {
                CategoryPromotion("Hygiene 10", "Hygiene", 20),
                CategoryPromotion("Meat 10", "Meat", 10)
            };
            var tiers = new[]
            {
                new PosPriceChannelTier(channelId, hygieneId, 1m, 90m, "COP"),
                new PosPriceChannelTier(channelId, meatId, 1m, 180m, "COP")
            };
            var customers = new[] { new PosCustomerPricing(customerId, "1", "Customer", channelId, true) };

            await store.ApplyPricingSnapshotAsync(new(
                [new(channelId,"TIER","Tiered", "TieredProductPrice",null)],tiers,[],customers,
                AllowPromotionChannelCombination: true, Promotions: categories));
            var combined = await store.ResolvePricesAsync(
                [new("h", hygieneId, 1m), new("m", meatId, 1m)], customerId);
            Assert.Equal(81m, combined["h"].Amount);
            Assert.Equal(162m, combined["m"].Amount);
            Assert.All(combined.Values, value => Assert.Equal("Promotion+PriceChannel", value.Source));

            await store.ApplyPricingSnapshotAsync(new(
                [new(channelId,"TIER","Tiered", "TieredProductPrice",null)],tiers,[],customers,
                AllowPromotionChannelCombination: false, Promotions: categories));
            var promotionsWin = await store.ResolvePricesAsync(
                [new("h", hygieneId, 1m), new("m", meatId, 1m)], customerId);
            Assert.Equal(90m, promotionsWin["h"].Amount);
            Assert.Equal(180m, promotionsWin["m"].Amount);
            Assert.All(promotionsWin.Values, value => Assert.Equal("Promotion", value.Source));

            var buyThree = new PosPromotion(
                Guid.NewGuid(), "Buy 2 get 1", 100, false, null, now.AddDays(-1), now.AddDays(1), now,
                [new((int)PromotionItemType.Product, hygieneId, null, null, 3m, null)],
                [new((int)PromotionBenefitType.FreeItem, (int)PromotionItemType.Product,
                    hygieneId, null, null, null, null, null, 1m)]);
            await store.ApplyPricingSnapshotAsync(new([], [], [], [], Promotions: [buyThree]));
            Assert.Equal(100m, (await store.ResolvePriceAsync(hygieneId, null, 2m)).Amount);
            Assert.Equal(200m / 3m, (await store.ResolvePriceAsync(hygieneId, null, 3m)).Amount);

            var threshold = new PosPromotion(
                Guid.NewGuid(), "Order 10", 50, true, null, now.AddDays(-1), now.AddDays(1), now,
                [new((int)PromotionItemType.Any, null, null, null, 0m, 300m)],
                [new((int)PromotionBenefitType.PercentageDiscount, (int)PromotionItemType.AnyProduct,
                    null, null, null, 10m, null, null, null)]);
            await store.ApplyPricingSnapshotAsync(new([], [], [], [], Promotions: [threshold]));
            var thresholdResult = await store.ResolvePricesAsync(
                [new("h", hygieneId, 1m), new("m", meatId, 1m)], null);
            Assert.Equal(90m, thresholdResult["h"].Amount);
            Assert.Equal(180m, thresholdResult["m"].Amount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

}
