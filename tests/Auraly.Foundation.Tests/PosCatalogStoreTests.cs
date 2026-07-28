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
