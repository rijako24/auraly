using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosDraftStoreTests
{
    [Fact]
    public async Task Active_sale_survives_restart_and_identical_scans_create_separate_lines()
    {
        await WithStoreAsync(async (store, path, scope, ids) =>
        {
            var input = Line(quantity: 1m);
            var first = await store.AddOrIncrementLineAsync(scope, input);
            var second = await store.AddOrIncrementLineAsync(scope, input);

            Assert.Equal(first.DraftId, second.DraftId);
            Assert.Equal(2, second.Lines.Count);
            Assert.All(second.Lines, line => Assert.Equal(1m, line.Quantity));
            Assert.Equal(2, second.Lines.Select(line => line.LineId).Distinct().Count());
            Assert.Equal(20_000m, second.PayableAmount);

            var reopened = Store(path, ids);
            await reopened.InitializeAsync();
            var recovered = await reopened.GetOrCreateActiveAsync(scope);
            Assert.Equal(first.DraftId, recovered.DraftId);
            Assert.Equal(2, recovered.Lines.Count);
            Assert.All(recovered.Lines, line => Assert.Equal(1m, line.Quantity));
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
                    PriceSource = "PriceChannel",
                    PriceChannelId = Guid.NewGuid()
                });

            Assert.Equal(2, draft.Lines.Count);
            Assert.Equal(26_000m, draft.PayableAmount);

            var edited = await store.SetQuantityAsync(
                draft.DraftId,
                draft.Lines[1].LineId,
                3m);
            Assert.Equal(34_000m, edited.PayableAmount);
            var discounted = await store.SetDiscountAsync(
                draft.DraftId,
                edited.Lines[1].LineId,
                1_000m);
            Assert.Equal(33_000m, discounted.PayableAmount);
            var removed = await store.RemoveLineAsync(
                draft.DraftId,
                discounted.Lines[0].LineId);
            Assert.Single(removed.Lines);
            Assert.Equal(23_000m, removed.PayableAmount);
        });
    }

    [Fact]
    public async Task Document_line_edits_are_atomic_and_preserve_catalog_price()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            var first = await store.AddOrIncrementLineAsync(scope, Line(quantity: 1m));
            var draft = await store.AddOrIncrementLineAsync(scope, Line(quantity: 2m));

            var updated = await store.UpdateLinesAsync(
                draft.DraftId,
                [
                    new(first.Lines.Single().LineId, "Descripción puntual", 12_000m, 1_000m),
                    new(draft.Lines[1].LineId, "Segunda línea", 9_000m, 0m)
                ]);

            Assert.Equal(29_000m, updated.PayableAmount);
            Assert.Equal("Descripción puntual", updated.Lines[0].Description);
            Assert.Equal(12_000m, updated.Lines[0].UnitPrice);
            Assert.Equal(10_000m, updated.Lines[0].BaseUnitPrice);
            Assert.Equal("ManualOverride", updated.Lines[0].PriceSource);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.UpdateLinesAsync(
                    draft.DraftId,
                    [new(updated.Lines[0].LineId, "Incompleta", 5_000m, 0m)]));
            var unchanged = await store.GetOrCreateActiveAsync(scope);
            Assert.Equal("Descripción puntual", unchanged.Lines[0].Description);
            Assert.Equal("Segunda línea", unchanged.Lines[1].Description);
        });
    }

    [Fact]
    public async Task Order_recovery_is_atomic_durable_and_uses_current_tax_configuration()
    {
        await WithStoreAsync(async (store, path, scope, ids) =>
        {
            var orderId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var orderCommercialLine = Line(2m) with
            {
                TaxCode = "VAT5",
                TaxRate = 5m,
                BaseUnitPrice = 8_000m,
                UnitPrice = 8_000m,
                Discount = 1_000m,
                PriceSource = "Order"
            };

            var imported = await store.ImportOrderAsync(
                scope,
                orderId,
                customerId,
                [orderCommercialLine]);

            Assert.Equal(orderId, imported.SourceOrderId);
            Assert.Equal(customerId, imported.CustomerId);
            Assert.Equal(14_285.71m, imported.UntaxedAmount);
            Assert.Equal(714.29m, imported.TaxAmount);
            Assert.Equal(15_000m, imported.PayableAmount);

            var reopened = Store(path, ids);
            await reopened.InitializeAsync();
            var recovered = await reopened.GetOrCreateActiveAsync(scope);
            Assert.Equal(orderId, recovered.SourceOrderId);
            Assert.Equal("VAT5", recovered.Lines.Single().TaxCode);
            Assert.Equal(5m, recovered.Lines.Single().TaxRate);
        });
    }

    [Fact]
    public async Task Order_recovery_never_mixes_with_the_current_sale()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            var current = await store.AddOrIncrementLineAsync(scope, Line(1m));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ImportOrderAsync(
                    scope,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    [Line(2m) with { TaxRate = 5m, TaxCode = "VAT5" }]));

            var unchanged = await store.GetAsync(current.DraftId);
            Assert.Single(unchanged!.Lines);
            Assert.Null(unchanged.SourceOrderId);
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
            Assert.Equal(19_500m, recovered.PayableAmount);

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
    public async Task Cancelling_a_draft_preserves_audit_and_creates_a_clean_sale()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            var active = await store.AddOrIncrementLineAsync(scope, Line(2m));

            await store.CancelAsync(active.DraftId);

            var cancelled = await store.GetAsync(active.DraftId);
            Assert.NotNull(cancelled);
            Assert.Equal(PosDraftStatus.Deleted, cancelled.Status);
            Assert.Empty(cancelled.Lines);
            Assert.Equal(0m, cancelled.PayableAmount);

            var next = await store.GetOrCreateActiveAsync(scope);
            Assert.NotEqual(active.DraftId, next.DraftId);
            Assert.Equal(PosDraftStatus.Active, next.Status);
            Assert.Empty(next.Lines);
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
                new DeviceId(Guid.NewGuid()),
                new WorkSessionId(Guid.NewGuid()),
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
