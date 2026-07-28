using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosDraftStoreTests
{
    [Fact]
    public async Task Active_sale_survives_restart_and_identical_scans_increment_one_line()
    {
        await WithStoreAsync(async (store, path, scope, ids) =>
        {
            var input = Line(quantity: 1m);
            var first = await store.AddOrIncrementLineAsync(scope, input);
            var second = await store.AddOrIncrementLineAsync(scope, input);

            Assert.Equal(first.DraftId, second.DraftId);
            Assert.Single(second.Lines);
            Assert.Equal(2m, second.Lines[0].Quantity);
            Assert.Equal(23_800m, second.PayableAmount);

            var reopened = Store(path, ids);
            await reopened.InitializeAsync();
            var recovered = await reopened.GetOrCreateActiveAsync(scope);
            Assert.Equal(first.DraftId, recovered.DraftId);
            Assert.Equal(2m, recovered.Lines.Single().Quantity);
        });
    }

    [Fact]
    public async Task Price_source_controls_merge_and_edits_recalculate_totals()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            await store.AddOrIncrementLineAsync(scope, Line(quantity: 1m));
            var draft = await store.AddOrIncrementLineAsync(
                scope,
                Line(quantity: 2m) with
                {
                    UnitPrice = 8_000m,
                    PriceSource = "PriceList",
                    PriceListId = Guid.NewGuid()
                });

            Assert.Equal(2, draft.Lines.Count);
            Assert.Equal(30_940m, draft.PayableAmount);

            var edited = await store.SetQuantityAsync(
                draft.DraftId,
                draft.Lines[1].LineId,
                3m);
            Assert.Equal(40_460m, edited.PayableAmount);
            var discounted = await store.SetDiscountAsync(
                draft.DraftId,
                edited.Lines[1].LineId,
                1_000m);
            Assert.Equal(39_270m, discounted.PayableAmount);
            var removed = await store.RemoveLineAsync(
                draft.DraftId,
                discounted.Lines[0].LineId);
            Assert.Single(removed.Lines);
            Assert.Equal(27_370m, removed.PayableAmount);
        });
    }

    [Fact]
    public async Task Temporary_sale_is_durable_recoverable_once_and_keeps_commercial_snapshot()
    {
        await WithStoreAsync(async (store, path, scope, ids) =>
        {
            var customerId = Guid.NewGuid();
            var sellerId = Guid.NewGuid();
            var active = await store.AddOrIncrementLineAsync(
                scope,
                Line(quantity: 2m) with { Discount = 500m });
            await store.AssignPartiesAsync(active.DraftId, customerId, sellerId);
            var temporary = await store.SaveTemporaryAsync(
                active.DraftId,
                "Mesa 4",
                "REF-44",
                "Cliente regresa");

            Assert.Equal(PosDraftStatus.Temporary, temporary.Status);
            Assert.Empty((await store.GetOrCreateActiveAsync(scope)).Lines);
            Assert.Single(await store.ListTemporariesAsync(
                scope.BusinessId,
                new PosTemporaryFilter(Search: "REF-44")));

            var reopened = Store(path, ids);
            await reopened.InitializeAsync();
            var recovered = await reopened.RecoverTemporaryAsync(temporary.DraftId, scope);
            Assert.Equal(PosDraftStatus.Active, recovered.Status);
            Assert.Equal(customerId, recovered.CustomerId);
            Assert.Equal(sellerId, recovered.SellerId);
            Assert.Equal(500m, recovered.Lines.Single().Discount);
            Assert.Equal(23_205m, recovered.PayableAmount);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => reopened.RecoverTemporaryAsync(temporary.DraftId, scope));
            Assert.Equal(
                PosDraftStatus.Consumed,
                (await reopened.GetAsync(temporary.DraftId))!.Status);
        });
    }

    [Fact]
    public async Task Temporary_cannot_replace_a_non_empty_active_sale()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            var temporary = await store.AddOrIncrementLineAsync(scope, Line(1m));
            await store.SaveTemporaryAsync(temporary.DraftId, "Pendiente", null, null);
            await store.AddOrIncrementLineAsync(
                scope,
                Line(1m) with { ProductId = new ProductId(Guid.NewGuid()) });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.RecoverTemporaryAsync(temporary.DraftId, scope));
        });
    }

    [Fact]
    public async Task Adding_draft_schema_preserves_existing_pos_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-draft-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IssuedSales(DocumentId TEXT PRIMARY KEY,FiscalNumber TEXT);
                    CREATE TABLE Outbox(MessageId TEXT PRIMARY KEY,DocumentId TEXT);
                    CREATE TABLE PosCatalogProducts(ProductId TEXT PRIMARY KEY,Name TEXT);
                    INSERT INTO IssuedSales VALUES('d1','FV1');
                    INSERT INTO Outbox VALUES('m1','d1');
                    INSERT INTO PosCatalogProducts VALUES('p1','Product');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var store = Store(path);
            await store.InitializeAsync();
            await store.InitializeAsync();

            await using var verification = new SqliteConnection($"Data Source={path}");
            await verification.OpenAsync();
            foreach (var table in new[] { "IssuedSales", "Outbox", "PosCatalogProducts" })
            {
                await using var command = verification.CreateCommand();
                command.CommandText = $"SELECT count(*) FROM {table};";
                Assert.Equal(1L, await command.ExecuteScalarAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Delete(path);
        }
    }

    private static async Task WithStoreAsync(
        Func<PosDraftStore, string, PosDraftScope, IAuralyIdGenerator, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-draft-{Guid.NewGuid():N}.db");
        try
        {
            var ids = new SequentialUuid7Generator();
            var store = Store(path, ids);
            await store.InitializeAsync();
            var scope = new PosDraftScope(
                new BusinessId(Guid.NewGuid()),
                new WarehouseId(Guid.NewGuid()),
                new RegisterId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()));
            await test(store, path, scope, ids);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Delete(path);
            Delete($"{path}-wal");
            Delete($"{path}-shm");
        }
    }

    private static PosDraftStore Store(string path, IAuralyIdGenerator? ids = null) =>
        new(
            $"Data Source={path}",
            ids ?? new SequentialUuid7Generator(),
            TimeProvider.System);

    private static PosDraftLineInput Line(decimal quantity) =>
        new(
            new ProductId(Guid.NewGuid()),
            "P-1",
            "Producto",
            "EA",
            "VAT19",
            19m,
            quantity,
            10_000m,
            10_000m,
            "COP",
            "Base");

    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class SequentialUuid7Generator : IAuralyIdGenerator
    {
        private long _counter;

        public Guid NewId()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            BitConverter.GetBytes(Interlocked.Increment(ref _counter)).CopyTo(bytes, 0);
            return new Guid(bytes);
        }
    }
}
