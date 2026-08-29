using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosCatalogStoreTests
{
    [Fact]
    public async Task Bootstrap_is_durable_atomic_and_preserves_existing_pos_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-catalog-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Invoices(DocumentId TEXT PRIMARY KEY); INSERT INTO Invoices VALUES('existing-sale');";
                await command.ExecuteNonQueryAsync();
            }

            var store = new PosCatalogStore(connectionString);
            await store.InitializeAsync();
            var session = new CatalogSyncSessionResponse(Guid.NewGuid(), 7, 1, DateTimeOffset.UtcNow.AddHours(1));
            await store.BeginBootstrapAsync(session);
            var item = Product();
            var page = Page(session, [item], hasMore: false, next: null);
            await store.ApplyBootstrapPageAsync(page);

            Assert.Null(await store.CaptureAsync("7701234567890"));
            var reopened = new PosCatalogStore(connectionString);
            var checkpoint = await reopened.StatusAsync();
            Assert.Equal("Bootstrapping", checkpoint.Status);
            Assert.Equal(session.SessionId, checkpoint.SessionId);

            await reopened.PromoteBootstrapAsync();
            var captured = await reopened.CaptureAsync("7701234567890");
            Assert.NotNull(captured);
            Assert.Equal(item.ProductId, captured.Product.ProductId);
            Assert.Equal(7, (await reopened.StatusAsync()).Cursor);

            await using var verify = new SqliteConnection(connectionString);
            await verify.OpenAsync();
            await using var verifyCommand = verify.CreateCommand();
            verifyCommand.CommandText = "SELECT COUNT(*) FROM Invoices WHERE DocumentId='existing-sale';";
            Assert.Equal(1L, (long)(await verifyCommand.ExecuteScalarAsync())!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Incremental_changes_are_idempotent_and_tombstones_block_new_capture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-catalog-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PosCatalogStore($"Data Source={path}");
            await store.InitializeAsync();
            var session = new CatalogSyncSessionResponse(Guid.NewGuid(), 4, 1, DateTimeOffset.UtcNow.AddHours(1));
            await store.BeginBootstrapAsync(session);
            await store.ApplyBootstrapPageAsync(Page(session, [Product()], false, null));
            await store.PromoteBootstrapAsync();

            var blocked = Product() with { Name = "Blocked", IsActive = false };
            var delta = new CatalogDeltaPage(4, 8, false, [new CatalogDelta(8, "Tombstone", blocked)]);
            await store.ApplyChangesAsync(delta);
            await store.ApplyChangesAsync(delta);

            Assert.Null(await store.CaptureAsync("7701234567890"));
            Assert.Equal(8, (await store.StatusAsync()).Cursor);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Capture_supports_name_identifiers_and_configured_scale_barcodes_offline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-catalog-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PosCatalogStore($"Data Source={path}");
            await store.InitializeAsync();
            var session = new CatalogSyncSessionResponse(Guid.NewGuid(), 1, 1, DateTimeOffset.UtcNow.AddHours(1));
            var item = Product() with
            {
                Scale = new ScaleConfigurationInput("12345", "20", "Weight", 7, 5, 3),
                Identifiers = [new ProductIdentifierInput("Alternate", "ALT-01")]
            };
            await store.BeginBootstrapAsync(session);
            await store.ApplyBootstrapPageAsync(Page(session, [item], false, null));
            await store.PromoteBootstrapAsync();

            Assert.Single(await store.SearchAsync("Coffee"));
            Assert.Single(await store.SearchAsync("fee"));
            Assert.Single(await store.SearchAsync("P-0"));
            Assert.Single(await store.SearchAsync("REF-0"));
            Assert.Single(await store.SearchAsync("7701234"));
            Assert.Single(await store.SearchAsync("ALT-"));
            Assert.Single(await store.SearchAsync("fee"));
            Assert.Single(await store.SearchAsync("P-0"));
            Assert.Single(await store.SearchAsync("REF-0"));
            Assert.Single(await store.SearchAsync("7701234"));
            Assert.Single(await store.SearchAsync("ALT-"));
            Assert.Equal(item.ProductId, (await store.CaptureAsync("ALT-01"))!.Product.ProductId);
            var weighed = await store.CaptureAsync("201234500250");
            Assert.NotNull(weighed);
            Assert.Equal(0.250m, weighed.Quantity);
            Assert.Equal("Scale", weighed.MatchKind);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Large_bootstrap_resumes_from_durable_checkpoint_without_duplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-large-catalog-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PosCatalogStore($"Data Source={path}");
            await store.InitializeAsync();
            var session = new CatalogSyncSessionResponse(Guid.NewGuid(), 1_500, 1_500, DateTimeOffset.UtcNow.AddHours(1));
            await store.BeginBootstrapAsync(session);
            var products = Enumerable.Range(1, 1_500)
                .Select(index => Product() with
                {
                    ProductId = Guid.NewGuid(),
                    ProductCode = $"P-{index:000000}",
                    Reference = $"R-{index:000000}",
                    Name = $"Product {index:000000}",
                    Barcodes = [$"770{index:0000000000}"]
                })
                .ToArray();

            for (var offset = 0; offset < 750; offset += 250)
            {
                var items = products.Skip(offset).Take(250).ToArray();
                await store.ApplyBootstrapPageAsync(Page(session, items, true, items[^1].ProductId.ToString("D")));
            }

            var reopened = new PosCatalogStore($"Data Source={path}");
            Assert.Equal(products[749].ProductId.ToString("D"), (await reopened.StatusAsync()).NextPageCursor);
            for (var offset = 750; offset < products.Length; offset += 250)
            {
                var items = products.Skip(offset).Take(250).ToArray();
                var hasMore = offset + items.Length < products.Length;
                await reopened.ApplyBootstrapPageAsync(Page(
                    session, items, hasMore, hasMore ? items[^1].ProductId.ToString("D") : null));
            }
            await reopened.PromoteBootstrapAsync();

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM PosCatalogProducts;";
            Assert.Equal(1_500L, (long)(await command.ExecuteScalarAsync())!);
            Assert.NotNull(await reopened.CaptureAsync("7700000001500"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Operational_reference_options_are_replaced_atomically_and_survive_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-pos-options-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={path}";
            var store = new PosCatalogStore(connectionString);
            await store.InitializeAsync();
            await store.ApplyReferenceOptionsAsync("payment-method",
            [
                new ReferenceOption(Guid.NewGuid(), "Cash", "Efectivo", null, 10),
                new ReferenceOption(Guid.NewGuid(), "DebitCard", "Tarjeta débito", null, 20)
            ]);

            var reopened = new PosCatalogStore(connectionString);
            var initial = await reopened.ReferenceOptionsAsync("payment-method");
            Assert.Equal(["Cash", "DebitCard"], initial.Select(option => option.Code));

            await reopened.ApplyReferenceOptionsAsync("payment-method",
            [
                new ReferenceOption(Guid.NewGuid(), "Transfer", "Transferencia", null, 5)
            ]);

            var replaced = await reopened.ReferenceOptionsAsync("payment-method");
            Assert.Single(replaced);
            Assert.Equal("Transfer", replaced[0].Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static PosCatalogItem Product() =>
        new(
            Guid.Parse("019ad1f0-8ec7-7e2f-a4d4-919f1c4cb080"),
            "P-001",
            "REF-001",
            "Coffee 500 g",
            "EA",
            "VAT19",
            19m,
            12500m,
            "COP",
            true,
            null,
            ["7701234567890"],
            []);

    private static CatalogBootstrapPage Page(
        CatalogSyncSessionResponse session,
        IReadOnlyCollection<PosCatalogItem> items,
        bool hasMore,
        string? next)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items))))
            .ToLowerInvariant();
        return new CatalogBootstrapPage(
            session.SessionId,
            session.HighWaterMark,
            next,
            hasMore,
            hash,
            items);
    }
}
