using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Auraly.Application.Sales;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosDraftStoreTests
{
    [Fact]
    public async Task Invoice_can_add_the_same_charge_definition_more_than_once()
    {
        await WithStoreAsync(async (store, path, scope, _) =>
        {
            var catalog = new PosCatalogStore($"Data Source={path};Pooling=False");
            await catalog.InitializeAsync();
            var definition = TestCharge(scope.BusinessId.Value) with
            {
                InclusionMode = "Always",
                InvoiceAmountLimit = null
            };
            await catalog.StageInvoiceChargePageAsync(
                scope.BusinessId.Value, 1, new([definition], 1, 100, 1, 1));
            await catalog.PromoteInvoiceChargesAsync(scope.BusinessId.Value, 1, 1);
            var draft = await store.AddOrIncrementLineAsync(scope, Line(1));
            var first = new InvoiceChargeDraftRequest(
                Guid.NewGuid(), definition.ChargeId, definition.Version,
                definition.Suppliers.Single().SupplierId, null, 0);
            var second = first with { AppliedChargeId = Guid.NewGuid() };

            await store.SaveChargeAsync(scope, draft.DraftId, first);
            var charged = await store.SaveChargeAsync(scope, draft.DraftId, second);

            Assert.Equal(2, charged.Charges!.Count);
            Assert.All(charged.Charges, charge => Assert.Equal(definition.ChargeId, charge.ChargeId));
            Assert.Equal(20_000m, charged.PayableAmount);
        });
    }

    [Fact]
    public async Task Invoice_charge_recalculates_from_invoice_amount_and_survives_restart_pause_and_recovery()
    {
        await WithStoreAsync(async (store, path, scope, ids) =>
        {
            var catalog = new PosCatalogStore($"Data Source={path};Pooling=False");
            await catalog.InitializeAsync();
            var definition = TestCharge(scope.BusinessId.Value);
            await catalog.StageInvoiceChargePageAsync(scope.BusinessId.Value, 1, new([definition], 1, 100, 1, 1));
            await catalog.PromoteInvoiceChargesAsync(scope.BusinessId.Value, 1, 1);
            var draft = await store.AddOrIncrementLineAsync(scope, Line(8));
            var request = new InvoiceChargeDraftRequest(Guid.NewGuid(), definition.ChargeId, 1,
                definition.Suppliers.Single().SupplierId, null, 0);
            var charged = await store.SaveChargeAsync(scope, draft.DraftId, request);
            Assert.Equal(85000, charged.PayableAmount);
            Assert.Equal(5000, Assert.Single(charged.Charges!).InvoicedAmount);
            var repriced = await store.SetQuantityAsync(draft.DraftId, draft.Lines.Single().LineId, 9);
            Assert.Equal(90000, repriced.PayableAmount);
            Assert.Equal(5000, Assert.Single(repriced.Charges!).ExpenseAmount);
            var otherCustomer = await store.AssignPartiesAsync(draft.DraftId, Guid.NewGuid(), null, Guid.NewGuid());
            Assert.Equal(repriced.Charges, otherCustomer.Charges);

            // Updating the catalog does not rewrite a selection already captured.
            await catalog.StageInvoiceChargePageAsync(scope.BusinessId.Value, 2,
                new([definition with { Version = 2, Value = 9000 }], 1, 100, 1, 1));
            await catalog.PromoteInvoiceChargesAsync(scope.BusinessId.Value, 2, 1);
            var reopened = Store(path, ids);
            var persisted = await reopened.GetOrCreateActiveAsync(scope);
            Assert.Equal(5000, Assert.Single(persisted.Charges!).Amount);
            await reopened.SaveTemporaryAsync(draft.DraftId, "Con domicilio", null, null);
            var paused = await reopened.ListTemporariesAsync(scope.BusinessId, new());
            Assert.Equal(5000, Assert.Single(Assert.Single(paused).Charges!).Amount);
            var recovered = await reopened.RecoverTemporaryAsync(draft.DraftId, scope);
            Assert.Equal(persisted.Charges, recovered.Charges);
            var empty = await reopened.RemoveLineAsync(recovered.DraftId, recovered.Lines.Single().LineId);
            Assert.Empty(empty.Charges!);
            Assert.Equal(0, empty.PayableAmount);
        });
    }

    [Fact]
    public async Task Invoice_charge_rejects_other_site_other_session_inactive_catalog_and_stale_version()
    {
        await WithStoreAsync(async (store, path, scope, _) =>
        {
            var catalog = new PosCatalogStore($"Data Source={path};Pooling=False");
            await catalog.InitializeAsync();
            var definition = TestCharge(scope.BusinessId.Value);
            await catalog.StageInvoiceChargePageAsync(scope.BusinessId.Value, 1, new([definition], 1, 100, 1, 1));
            await catalog.PromoteInvoiceChargesAsync(scope.BusinessId.Value, 1, 1);
            var draft = await store.AddOrIncrementLineAsync(scope, Line(1));
            var request = new InvoiceChargeDraftRequest(Guid.NewGuid(), definition.ChargeId, 1,
                definition.Suppliers.Single().SupplierId, null, 0);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveChargeAsync(
                scope with { BusinessId = new(Guid.NewGuid()) }, draft.DraftId, request));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveChargeAsync(
                scope with { UserId = new(Guid.NewGuid()) }, draft.DraftId, request));
            await Assert.ThrowsAsync<InvoiceChargeConflictException>(() => store.SaveChargeAsync(
                scope, draft.DraftId, request with { ChargeVersion = 2 }));
            await Assert.ThrowsAsync<InvoiceChargeValidationException>(() => store.SaveChargeAsync(
                scope, draft.DraftId, request with { SupplierId = Guid.NewGuid() }));
            var saved = await store.SaveChargeAsync(scope, draft.DraftId, request);
            var repeated = await store.SaveChargeAsync(scope, draft.DraftId, request);
            Assert.Single(repeated.Charges!);
            Assert.Equal(saved.PayableAmount, repeated.PayableAmount);
            await catalog.StageInvoiceChargePageAsync(scope.BusinessId.Value, 2, new([], 1, 100, 0, 0));
            await catalog.PromoteInvoiceChargesAsync(scope.BusinessId.Value, 2, 0);
            await Assert.ThrowsAsync<InvoiceChargeValidationException>(() => store.SaveChargeAsync(scope, draft.DraftId, request));
            var removed = await store.RemoveChargeAsync(scope, draft.DraftId, request.AppliedChargeId);
            Assert.Empty(removed.Charges!);
            Assert.Equal(10000, removed.PayableAmount);
        });
    }

    private static InvoiceChargeDefinition TestCharge(Guid businessId) => new(
        Guid.NewGuid(), businessId, 1, "DOM", "Domicilio de prueba", true, 0, "Fixed", 5000,
        "UpToInvoiceAmount", 80000, Guid.NewGuid(), "Domicilios", Guid.NewGuid(), "TEST", "Gasto",
        null, null, Guid.NewGuid(), "Impuesto", "01", 0, [],
        [new(Guid.NewGuid(), "Proveedor de prueba", "TEST", 0, true)], Guid.NewGuid(), "Impuesto", 0);

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
    public async Task Document_line_edits_are_atomic_and_allow_document_cost_only_when_the_line_supports_it()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            var first = await store.AddOrIncrementLineAsync(scope, Line(quantity: 1m) with { DocumentUnitCost = 4_000m, AllowsDocumentCostOverride = true });
            var draft = await store.AddOrIncrementLineAsync(scope, Line(quantity: 2m) with { DocumentUnitCost = 4_000m, AllowsDocumentCostOverride = true });

            var updated = await store.UpdateLinesAsync(
                draft.DraftId,
                [
                    new(first.Lines.Single().LineId, "Descripción puntual", 13_000m, 0m, 4_500m),
                    new(draft.Lines[1].LineId, "Segunda línea", 10_000m, 0m, 4_000m)
                ]);

            Assert.Equal(33_000m, updated.PayableAmount);
            Assert.Equal("Descripción puntual", updated.Lines[0].Description);
            Assert.Equal(13_000m, updated.Lines[0].UnitPrice);
            Assert.Equal(13_000m, updated.Lines[0].PublicUnitPrice);
            Assert.Equal(10_000m, updated.Lines[0].BaseUnitPrice);
            Assert.Equal(4_500m, updated.Lines[0].DocumentUnitCost);
            Assert.True(updated.Lines[0].IsPriceOverridden);
            Assert.Equal("Manual", updated.Lines[0].PriceSource);

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
    public async Task Captured_document_cost_is_frozen_when_the_product_manages_inventory()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            var draft = await store.AddOrIncrementLineAsync(scope, Line(1m) with { DocumentUnitCost = 4_000m });
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateLinesAsync(
                draft.DraftId,
                [new(draft.Lines.Single().LineId, "Producto", 10_000m, 0m, 5_000m)]));
        });
    }

    [Fact]
    public async Task Only_an_unpriced_generic_line_can_be_discarded_without_sensitive_authorization()
    {
        await WithStoreAsync(async (store, _, scope, _) =>
        {
            var generic = await store.AddOrIncrementLineAsync(scope, Line(1m) with
            {
                UnitPrice = 0m,
                BaseUnitPrice = 0m,
                DocumentUnitCost = 0m,
                AllowsDocumentCostOverride = true
            });
            var discarded = await store.DiscardUnpricedGenericLineAsync(
                generic.DraftId, generic.Lines.Single().LineId);
            Assert.Empty(discarded.Lines);

            var priced = await store.AddOrIncrementLineAsync(scope, Line(1m) with
            {
                DocumentUnitCost = 4_000m,
                AllowsDocumentCostOverride = true
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.DiscardUnpricedGenericLineAsync(
                    priced.DraftId, priced.Lines.Single().LineId));
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
                Line(quantity: 2m) with
                {
                    Discount = 0m,
                    DocumentUnitCost = 4_000m,
                    AllowsDocumentCostOverride = true
                });
            active = await store.UpdateLinesAsync(
                active.DraftId,
                [new(active.Lines.Single().LineId, "Nombre editado antes de pausar", 10_000m, 0m, 4_500m)]);
            var customerPartySiteId = Guid.NewGuid();
            await store.AssignPartiesAsync(
                active.DraftId, customerId, sellerId, customerPartySiteId);
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
            Assert.Equal(customerPartySiteId, recovered.CustomerPartySiteId);
            Assert.Equal(sellerId, recovered.SellerId);
            var recoveredLine = recovered.Lines.Single();
            Assert.Equal("Nombre editado antes de pausar", recoveredLine.Description);
            Assert.Equal(4_500m, recoveredLine.DocumentUnitCost);
            Assert.Equal(10_000m, recoveredLine.UnitPrice);
            Assert.Equal(0m, recoveredLine.Discount);
            Assert.Equal(20_000m, recovered.PayableAmount);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => reopened.RecoverTemporaryAsync(temporary.DraftId, scope));
            Assert.Equal(
                PosDraftStatus.Consumed,
                (await reopened.GetAsync(temporary.DraftId))!.Status);
        });
    }

    [Fact]
    public async Task Temporary_sale_preserves_the_closed_line_total_without_reconstructing_it()
    {
        await WithStoreAsync(async (store, path, scope, ids) =>
        {
            var active = await store.AddOrIncrementLineAsync(
                scope,
                Line(0.3m) with
                {
                    UnitPrice = 15_340.45m,
                    PublicLineTotal = 4_602.13m
                });
            var temporary = await store.SaveTemporaryAsync(
                active.DraftId, "Fracción", null, null);

            var reopened = Store(path, ids);
            await reopened.InitializeAsync();
            var recovered = await reopened.RecoverTemporaryAsync(
                temporary.DraftId, scope);

            var line = Assert.Single(recovered.Lines);
            Assert.Equal(4_602.13m, line.PublicLineTotal);
            Assert.Equal(4_602.13m, recovered.PayableAmount);
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
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
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

            await using var verification = new SqliteConnection($"Data Source={path};Pooling=False");
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
            $"Data Source={path};Pooling=False",
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
