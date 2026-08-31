using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Domain.Authorization;
using Auraly.Fiscal.Core;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosSaleCompletionServiceTests
{
    [Fact]
    public async Task Recovered_order_is_preserved_in_the_durable_sale_upload()
    {
        await WithFixtureAsync(async fixture =>
        {
            var orderId = Guid.NewGuid();
            var draft = await fixture.Drafts.ImportOrderAsync(
                fixture.Scope,
                orderId,
                Guid.NewGuid(),
                [new PosDraftLineInput(
                    new ProductId(Guid.NewGuid()),
                    "P-ORDER",
                    "Producto pedido",
                    "EA",
                    "01",
                    19m,
                    1m,
                    10_000m,
                    10_000m,
                    "COP",
                    "BusinessDefault")]);

            await fixture.CompleteAsync(draft.DraftId);

            var outbox = Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
            var upload = PosSaleContractSerializer.Deserialize(outbox.Payload);
            Assert.Equal(orderId, upload.SourceOrderId);
        });
    }
    [Fact]
    public async Task Successful_print_clears_sale_and_previews_the_next_number()
    {
        await WithFixtureAsync(async fixture =>
        {
            var draft = await fixture.AddLineAsync();
            var before = await fixture.Sales.PreviewNextFiscalNumberAsync(
                fixture.Scope.DeviceId,
                fixture.IssuedAt);
            Assert.Equal("FV100", before.FullNumber);
            Assert.Equal(
                "VTA03-00000100",
                (await fixture.Sales.PreviewNextDocumentNumberAsync(
                    fixture.Scope.DeviceId, AuralyDocumentTypes.SalesInvoice)).FullNumber);

            var result = await fixture.CompleteAsync(draft.DraftId);

            Assert.Equal("VTA03-00000100", result.IssuedSale.DocumentNumber);
            Assert.Equal("FV100", result.IssuedSale.FiscalNumber);
            Assert.Equal(result.IssuedSale.Cufe, fixture.Printer.Receipts.Single().Cufe);
            Assert.Equal(result.IssuedSale.QrPayload, fixture.Printer.Receipts.Single().QrPayload);
            Assert.Contains("NumFac: FV100", fixture.Printer.Receipts.Single().QrPayload);
            Assert.Equal(PosDraftStatus.Consumed, (await fixture.Drafts.GetAsync(draft.DraftId))!.Status);
            Assert.NotEqual(draft.DraftId, result.NextDraft.DraftId);
            Assert.Empty(result.NextDraft.Lines);
            Assert.Equal("VTA03-00000101", result.NextDocumentNumber.FullNumber);
            Assert.NotNull(result.NextFiscalNumber);
            Assert.Equal("FV101", result.NextFiscalNumber.FullNumber);
            Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
        });
    }

    [Fact]
    public async Task Print_failure_keeps_issued_sale_and_explicit_reprint_does_not_renumber()
    {
        await WithFixtureAsync(async fixture =>
        {
            var draft = await fixture.AddLineAsync();
            fixture.Printer.Fail = true;

            var issued = await fixture.CompleteAsync(draft.DraftId);

            var afterFailure = await fixture.Drafts.GetAsync(draft.DraftId);
            Assert.Equal(PosDraftStatus.Consumed, afterFailure!.Status);
            Assert.False(issued.PrintedDirectly);
            Assert.Contains("printer", issued.PrintError, StringComparison.OrdinalIgnoreCase);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Drafts.SetQuantityAsync(
                    draft.DraftId,
                    draft.Lines.Single().LineId,
                    2m));
            Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
            Assert.Equal(
                "FV101",
                (await fixture.Sales.PreviewNextFiscalNumberAsync(
                    fixture.Scope.DeviceId,
                    fixture.IssuedAt)).FullNumber);

            fixture.Printer.Fail = false;
            await new PosSaleCompletionService(
                fixture.Drafts, fixture.Issuance, fixture.Sales, fixture.Printer)
                .ReprintAsync(issued.IssuedSale.DocumentId, fixture.Scope.UserId, 80);

            Assert.Equal("VTA03-00000100", issued.IssuedSale.DocumentNumber);
            Assert.Equal("FV100", issued.IssuedSale.FiscalNumber);
            Assert.Single(fixture.Printer.Receipts);
            Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
        });
    }

    [Fact]
    public async Task Invalid_payments_do_not_issue_or_consume_a_number()
    {
        await WithFixtureAsync(async fixture =>
        {
            var draft = await fixture.AddLineAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.CompleteAsync(
                    draft.DraftId,
                    [new OfflineSalePayment("Cash", 1m)]));

            Assert.Equal(
                "VTA03-00000100",
                (await fixture.Sales.PreviewNextDocumentNumberAsync(
                    fixture.Scope.DeviceId, AuralyDocumentTypes.SalesInvoice)).FullNumber);
            Assert.Equal(
                "FV100",
                (await fixture.Sales.PreviewNextFiscalNumberAsync(
                    fixture.Scope.DeviceId,
                    fixture.IssuedAt)).FullNumber);
            Assert.Empty(await fixture.Sales.GetPendingOutboxAsync());
            Assert.Equal(PosDraftStatus.Active, (await fixture.Drafts.GetAsync(draft.DraftId))!.Status);
            Assert.Empty(fixture.Printer.Receipts);
        });
    }

    private static async Task WithFixtureAsync(Func<Fixture, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-completion-{Guid.NewGuid():N}.db");
        try
        {
            var fixture = await Fixture.CreateAsync(path);
            await test(fixture);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Delete(path);
            Delete($"{path}-wal");
            Delete($"{path}-shm");
        }
    }

    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class Fixture
    {
        private Fixture(
            PosDraftScope scope,
            SalesExecutionContext register,
            PosDraftStore drafts,
            PosDraftIssuanceStore issuance,
            PosEdgeSaleStore sales,
            RecordingPrinter printer)
        {
            Scope = scope;
            Register = register;
            Drafts = drafts;
            Issuance = issuance;
            Sales = sales;
            Printer = printer;
        }

        public DateTimeOffset IssuedAt { get; } =
            new(2026, 7, 28, 14, 30, 0, TimeSpan.FromHours(-5));
        public PosDraftScope Scope { get; }
        public SalesExecutionContext Register { get; }
        public PosDraftStore Drafts { get; }
        public PosDraftIssuanceStore Issuance { get; }
        public PosEdgeSaleStore Sales { get; }
        public RecordingPrinter Printer { get; }

        public static async Task<Fixture> CreateAsync(string path)
        {
            var ids = new TestIdGenerator();
            var tenantId = new TenantId(Guid.NewGuid());
            var businessId = new BusinessId(Guid.NewGuid());
            var warehouseId = new WarehouseId(Guid.NewGuid());
            var deviceId = new DeviceId(Guid.NewGuid());
            var userId = new UserId(Guid.NewGuid());
            var workSessionId = new WorkSessionId(Guid.NewGuid());
            var scope = new PosDraftScope(businessId, warehouseId, deviceId, workSessionId, userId);
            var executionContext = new SalesExecutionContext(
                tenantId,
                businessId,
                warehouseId,
                userId,
                deviceId,
                workSessionId,
                true);
            var permissions = new UserPermissionSet(
                tenantId,
                userId,
                [CommercePermissionCodes.SalesCreate]);
            var confirmation = new ConfirmOfflineSaleService(
                new PermissionAuthorizer(new FixedPermissionProvider(permissions)));
            var connectionString = $"Data Source={path}";
            var drafts = new PosDraftStore(connectionString, ids, TimeProvider.System);
            var issuance = new PosDraftIssuanceStore(connectionString, ids, TimeProvider.System);
            var sales = new PosEdgeSaleStore(connectionString, confirmation);
            await sales.InitializeAsync();
            await drafts.InitializeAsync();
            await issuance.InitializeAsync();
            await sales.ProvisionDocumentSeriesAsync(new PosEdgeDocumentSeriesProvision(
                Guid.NewGuid(),
                deviceId,
                AuralyDocumentTypes.SalesInvoice,
                "VTA",
                "03",
                8,
                100,
                99_999_999));
            await sales.ProvisionSeriesAsync(new PosEdgeSeriesProvision(
                Guid.NewGuid(),
                deviceId,
                "FV",
                "18760000001",
                100,
                200,
                new DateOnly(2027, 7, 28)));
            return new Fixture(scope, executionContext, drafts, issuance, sales, new RecordingPrinter());
        }

        public Task<PosDraft> AddLineAsync() =>
            Drafts.AddOrIncrementLineAsync(
                Scope,
                new PosDraftLineInput(
                    new ProductId(Guid.NewGuid()),
                    "P-001",
                    "Producto",
                    "EA",
                    "01",
                    19m,
                    1m,
                    10_000m,
                    10_000m,
                    "COP",
                    "BusinessDefault"));

        public Task<CompletePosSaleResult> CompleteAsync(
            DraftId draftId,
            IReadOnlyCollection<OfflineSalePayment>? payments = null) =>
            new PosSaleCompletionService(Drafts, Issuance, Sales, Printer).CompleteAsync(
                draftId,
                new CompletePosSaleCommand(
                    Scope.UserId,
                    Register,
                    IssuedAt,
                    "9001234567",
                    "222222222",
                    new FiscalTechnicalKey("CLAVE-TECNICA", "v1"),
                    FiscalEnvironment.Test,
                    "https://catalogo-vpfe.dian.gov.co/document/searchqr",
                    payments ?? [new OfflineSalePayment("Cash", 10_000m)],
                    80));
    }

    private sealed class RecordingPrinter : IPosReceiptPrinter
    {
        public bool Fail { get; set; }
        public List<PosReceipt> Receipts { get; } = [];

        public Task PrintAsync(PosReceipt receipt, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Printer unavailable.");
            Receipts.Add(receipt);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedPermissionProvider(UserPermissionSet permissions)
        : IUserPermissionSetProvider
    {
        public UserPermissionSet Get(TenantId tenantId, UserId userId) => permissions;
    }

    private sealed class TestIdGenerator : IAuralyIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
