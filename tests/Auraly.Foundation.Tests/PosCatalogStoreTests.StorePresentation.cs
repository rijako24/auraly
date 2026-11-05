using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed partial class PosCatalogStoreTests
{
    [Fact]
    public async Task Legacy_catalog_requests_one_bootstrap_and_preserves_sales_until_atomic_promotion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-store-upgrade-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            var store = new PosCatalogStore(connectionString);
            await store.InitializeAsync();
            var session = new CatalogSyncSessionResponse(Guid.NewGuid(), 7, 1, DateTimeOffset.UtcNow.AddHours(1));
            await store.BeginBootstrapAsync(session);
            await store.ApplyBootstrapPageAsync(Page(session, [Product()], false, null));
            await store.PromoteBootstrapAsync();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    ALTER TABLE PosCatalogProducts DROP COLUMN PresentationJson;
                    CREATE TABLE Invoices(DocumentId TEXT PRIMARY KEY);
                    INSERT INTO Invoices VALUES('existing-sale');
                    """;
                await command.ExecuteNonQueryAsync();
            }
            var upgraded = new PosCatalogStore(connectionString);
            await upgraded.InitializeAsync();
            Assert.Equal("Invalid", (await upgraded.StatusAsync()).Status);
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Invoices WHERE DocumentId='existing-sale';";
                Assert.Equal(1L, await command.ExecuteScalarAsync());
            }
            await upgraded.BeginBootstrapAsync(session);
            await upgraded.ApplyBootstrapPageAsync(Page(session, [Product()], false, null));
            await upgraded.PromoteBootstrapAsync();
            await upgraded.InitializeAsync();
            Assert.Equal("Ready", (await upgraded.StatusAsync()).Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Store_presentation_and_family_pages_survive_restart_without_changing_inventory_identity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-store-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PosCatalogStore($"Data Source={path}");
            await store.InitializeAsync();
            var familyId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var items = Enumerable.Range(0, 60).Select(index => Product() with
            {
                ProductId = index == 0 ? familyId : Guid.NewGuid(), ProductCode = $"SHIRT-{index:000}",
                Name = $"Camiseta {index:000}", Barcodes = [$"STORE-{index:000}"], Identifiers = [],
                CategoryName = "Camisetas", ProductCategoryId = categoryId,
                Presentation = new("https://example.test/shirt.png", familyId, Guid.NewGuid(), "Negro", Guid.NewGuid(), $"Talla {index}"),
            }).ToArray();
            var session = new CatalogSyncSessionResponse(Guid.NewGuid(), 60, items.Length, DateTimeOffset.UtcNow.AddHours(1));
            await store.BeginBootstrapAsync(session);
            await store.ApplyBootstrapPageAsync(Page(session, items, false, null));
            await store.PromoteBootstrapAsync();
            await store.ApplyPricingSnapshotAsync(new([], [], [], [], PointOfSaleMode: "Store"));
            var reopened = new PosCatalogStore($"Data Source={path}");
            await reopened.InitializeAsync();
            Assert.Equal("Store", await reopened.PointOfSaleModeAsync());
            Assert.Equal(categoryId, Assert.Single(await reopened.CategoriesAsync()).CategoryId);
            var first = await reopened.SearchAsync("", 0, 50, familyProductId: items[1].ProductId);
            var next = await reopened.SearchAsync("", 50, 50, familyProductId: items[1].ProductId);
            Assert.Equal(50, first.Count);
            Assert.Equal(10, next.Count);
            Assert.Empty(first.Select(item => item.ProductId).Intersect(next.Select(item => item.ProductId)));
            Assert.All(first, item => Assert.Equal(familyId, item.Presentation!.FamilyProductId));
            Assert.Empty(await reopened.SearchAsync("", categoryId: Guid.NewGuid()));
            var captured = (await reopened.CaptureAsync("STORE-001"))!;
            Assert.Equal(items[1].ProductId, captured.Product.InventoryProductId);
            Assert.Equal(items[1].Presentation, captured.Product.Presentation);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
