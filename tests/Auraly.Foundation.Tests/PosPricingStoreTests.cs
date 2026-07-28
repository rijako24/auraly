using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Foundation.Tests;

public sealed class PosPricingStoreTests
{
    [Fact]
    public async Task Local_resolver_uses_list_then_channel_and_always_falls_back_to_business_price()
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

            var listId = Guid.NewGuid();
            var channelId = Guid.NewGuid();
            var listCustomer = Guid.NewGuid();
            var channelCustomer = Guid.NewGuid();
            var excludedCustomer = Guid.NewGuid();
            await store.ApplyPricingSnapshotAsync(new PosPricingSnapshot(
                [
                    new(listId, productId, 1m, 90m, "COP"),
                    new(listId, productId, 5m, 80m, "COP")
                ],
                [
                    new(channelId, productId, 85m, "COP", false)
                ],
                [
                    new(listCustomer, "1", "List customer", listId, null, true),
                    new(channelCustomer, "2", "Channel customer", null, channelId, true),
                    new(excludedCustomer, "3", "Missing channel item", null, Guid.NewGuid(), true)
                ]));

            var listOne = await store.ResolvePriceAsync(productId, listCustomer, 1m);
            Assert.Equal("PriceList", listOne.Source);
            Assert.Equal(90m, listOne.Amount);
            var listFive = await store.ResolvePriceAsync(productId, listCustomer, 5m);
            Assert.Equal(80m, listFive.Amount);

            var channel = await store.ResolvePriceAsync(productId, channelCustomer, 1m);
            Assert.Equal("PriceChannel", channel.Source);
            Assert.Equal(85m, channel.Amount);

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
    public async Task Local_customer_cannot_have_price_list_and_channel_at_the_same_time()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-pricing-exclusive-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PosCatalogStore($"Data Source={path}");
            await store.InitializeAsync();
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
                store.ApplyPricingSnapshotAsync(new PosPricingSnapshot(
                    [],
                    [],
                    [new(Guid.NewGuid(), "1", "Invalid", Guid.NewGuid(), Guid.NewGuid(), true)])));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
