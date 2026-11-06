using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed partial class PosCatalogStoreTests
{
    public static IEnumerable<object[]> RequiredCurrentProductColumns()
    {
        var fields = new (string Name, bool Additive)[]
        {
            ("ProductId", false), ("ProductCode", false), ("Reference", false), ("Name", false),
            ("BaseUnitCode", false), ("TaxCode", false), ("TaxRate", false), ("UnitPrice", false),
            ("UnitCost", true), ("ManagesStock", true), ("CurrencyCode", false), ("IsActive", false),
            ("IsWeighable", true), ("AllowsFractionalSale", true), ("ScaleJson", false), ("ScalePrefix", false),
            ("CategoryName", true), ("ProductCategoryId", true), ("ProductBrandId", true),
            ("ProductCategoryAncestorIds", true), ("AverageUnitCost", true), ("LatestUnitCost", true),
            ("TargetMarginPercent", true), ("InventoryProductId", true), ("InventoryFactor", true),
            ("IsGenericProduct", true), ("IsIngredient", true), ("HasIngredients", true), ("IsCombo", true),
            ("PresentationJson", true), ("FamilyProductId", true)
        };
        foreach (var table in new[] { "PosCatalogProducts", "PosCatalogStagingProducts" })
            foreach (var field in fields)
                yield return [table, field.Name, field.Additive];
    }

    [Theory]
    [MemberData(nameof(RequiredCurrentProductColumns))]
    public async Task Every_required_column_is_checked_before_the_current_catalog_can_be_ready(
        string table, string column, bool additive)
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
                var removeColumn = column == "ProductId"
                    ? $"ALTER TABLE {table} RENAME COLUMN ProductId TO RemovedProductId;"
                    : $"ALTER TABLE {table} DROP COLUMN {column};";
                command.CommandText = $"""
                    DROP INDEX IF EXISTS IX_{table}_InventoryProductId;
                    DROP INDEX IF EXISTS IX_{table}_Family;
                    DROP INDEX IF EXISTS IX_{table}_Category;
                    DROP INDEX IF EXISTS IX_PosCatalogProducts_Search;
                    {removeColumn}
                    CREATE TABLE Invoices(DocumentId TEXT PRIMARY KEY);
                    INSERT INTO Invoices VALUES('existing-sale');
                    """;
                await command.ExecuteNonQueryAsync();
            }
            var upgraded = new PosCatalogStore(connectionString);
            if (additive)
                await upgraded.InitializeAsync();
            else
            {
                var exception = await Assert.ThrowsAsync<InvalidDataException>(() => upgraded.InitializeAsync());
                Assert.Contains(column, exception.Message, StringComparison.Ordinal);
            }
            Assert.Equal("Invalid", (await upgraded.StatusAsync()).Status);
            if (additive)
            {
                var updatedProduct = Product() with
                {
                    UnitCost = 7_000m,
                    Presentation = new("https://example.test/coffee.png", Product().ProductId,
                        Guid.NewGuid(), "Rojo", Guid.NewGuid(), "500 g")
                };
                var refreshSession = session with { SessionId = Guid.NewGuid(), HighWaterMark = 8 };
                await upgraded.BeginBootstrapAsync(refreshSession);
                await upgraded.ApplyBootstrapPageAsync(Page(refreshSession, [updatedProduct], false, null));
                await upgraded.PromoteBootstrapAsync();
                var loaded = await upgraded.GetByProductIdAsync(updatedProduct.ProductId);
                Assert.NotNull(loaded);
                Assert.Equal(updatedProduct.Presentation, loaded.Presentation);
                Assert.Equal(updatedProduct.UnitPrice, loaded.UnitPrice);
                Assert.Equal(updatedProduct.UnitCost, loaded.UnitCost);
                Assert.Equal(updatedProduct.TaxRate, loaded.TaxRate);
                Assert.Equal(8, (await upgraded.StatusAsync()).Cursor);
                await upgraded.InitializeAsync();
                Assert.Equal("Ready", (await upgraded.StatusAsync()).Status);
            }
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Invoices WHERE DocumentId='existing-sale';";
                Assert.Equal(1L, await command.ExecuteScalarAsync());
            }
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
