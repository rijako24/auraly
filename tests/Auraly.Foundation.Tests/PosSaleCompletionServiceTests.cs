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
    public static IEnumerable<object[]> InvoiceChargeScenarios()
    {
        foreach (var (basis, tariff) in new[] { (79999m, 5000m), (80000m, 5000m), (80000.01m, 5000m), (100000m, 6000m), (400000m, 8000m) })
        foreach (var mode in new[] { "None", "Delivery", "Agotados" })
        foreach (var payment in new[] { "Cash", "TransferAndCredit" })
            yield return [basis, tariff, mode, payment];
    }

    [Theory, MemberData(nameof(InvoiceChargeScenarios))]
    public async Task Invoice_charge_is_durable_in_fiscal_totals_outbox_and_reprint(
        decimal basis, decimal tariff, string mode, string payment)
    {
        await WithFixtureAsync(async fixture =>
        {
            var draft = await fixture.AddLineAsync();
            draft = await fixture.Drafts.SetQuantityAsync(draft.DraftId, draft.Lines.Single().LineId, basis / 10000m);
            var customerId = Guid.NewGuid();
            draft = await fixture.Drafts.AssignPartiesAsync(draft.DraftId, customerId, null, Guid.NewGuid());
            var expectedCharge = mode == "Delivery" && basis <= 80000 ? tariff : 0;
            if (mode != "None")
            {
                var catalog = new PosCatalogStore(fixture.ConnectionString);
                await catalog.InitializeAsync();
                var definition = new InvoiceChargeDefinition(Guid.NewGuid(), fixture.Scope.BusinessId.Value,
                    1, "DOM", "Domicilio de prueba", true, 0, mode == "Agotados" ? "Manual" : "Ranges",
                    mode == "Agotados" ? 5000 : null, mode == "Agotados" ? "Never" : "UpToInvoiceAmount",
                    mode == "Agotados" ? null : 80000, Guid.NewGuid(), "Domicilios", Guid.NewGuid(), "TEST", "Gasto",
                    null, null, Guid.NewGuid(), "IVA 19", "01", 19,
                    mode == "Agotados" ? [] : [new(0,100000,"Fixed",5000),new(100000,200000,"Fixed",6000),
                        new(200000,300000,"Fixed",7000),new(300000,400000,"Fixed",8000),new(400000,null,"Percentage",2)],
                    [new(Guid.NewGuid(), "Domiciliario de prueba", "TEST", 0, true)], Guid.NewGuid(), "IVA 0", 0);
                await catalog.StageInvoiceChargePageAsync(fixture.Scope.BusinessId.Value, 1, new([definition],1,100,1,1));
                await catalog.PromoteInvoiceChargesAsync(fixture.Scope.BusinessId.Value, 1, 1);
                draft = await fixture.Drafts.SaveChargeAsync(fixture.Scope, draft.DraftId,
                    new(Guid.NewGuid(), definition.ChargeId, 1, definition.Suppliers.Single().SupplierId,
                        mode == "Agotados" ? 6500 : null, 0));
                Assert.Equal(expectedCharge, Assert.Single(draft.Charges!).InvoicedAmount);
            }
            var total = basis + expectedCharge;
            var roundingAdjustment = PosPaymentRoundingPolicy.Adjustment(total);
            var roundedTotal = total + roundingAdjustment;
            var transfer = decimal.Round(total / 4, 2);
            var payments = payment == "Cash"
                ? new[] { new OfflineSalePayment("Cash", total, TenderedAmount: roundedTotal + 10000,
                    RoundingAdjustment: roundingAdjustment) }
                : [new OfflineSalePayment("Transfer", transfer, "Referencia de prueba",
                    RoundingAdjustment: roundingAdjustment)];
            var credit = payment == "Cash" ? null : new PosSaleCreditTerms(customerId, total - transfer, fixture.IssuedAt.AddDays(15));
            var result = await fixture.CompleteAsync(draft.DraftId, payments, credit);
            Assert.Equal(roundedTotal, result.IssuedSale.Total);
            Assert.Equal(total, result.Receipt.Lines.Sum(line => line.Total));
            Assert.Empty(result.NextDraft.Charges!);
            var pending = Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
            var snapshot = PosSaleContractSerializer.Deserialize(pending.Payload);
            Assert.Equal(roundedTotal, snapshot.CommercialSnapshot.PayableAmount);
            Assert.Equal(roundingAdjustment, snapshot.CommercialSnapshot.PayableRoundingAmount);
            Assert.Equal(total, snapshot.Payments.Sum(value => value.Amount) + (snapshot.Credit?.Amount ?? 0));
            Assert.Null(Auraly.Application.Fiscal.FiscalSnapshotValidator.ValidateStructure(snapshot));
            Assert.Equal(mode == "None" ? 0 : 1, snapshot.Charges?.Count ?? 0);
            var localClosureSale = Assert.Single(await fixture.Sales.ReadWorkSessionSalesAsync(snapshot.WorkSessionId));
            Assert.Equal(roundedTotal, localClosureSale.Total);
            Assert.Equal(mode == "None" ? 0 : 1, localClosureSale.InvoiceCharges!.Count);
            Assert.Equal(expectedCharge, localClosureSale.InvoiceCharges.Sum(charge => charge.InvoicedAmount));
            Assert.Equal(expectedCharge, localClosureSale.InvoiceCharges.SelectMany(charge => charge.Payments).Sum(value => value.Amount));
            if (mode != "None")
            {
                var charge = Assert.Single(snapshot.Charges!);
                Assert.Equal(mode == "Agotados" ? 6500 : tariff, charge.Amount);
                Assert.Equal(charge.Amount - expectedCharge, charge.ExpenseAmount);
            }
            await new PosSaleCompletionService(fixture.Drafts, fixture.Issuance, fixture.Sales, fixture.Printer)
                .ReprintAsync(result.IssuedSale.DocumentId, fixture.Scope.UserId, 58);
            var printed = Assert.Single(fixture.Printer.Receipts);
            Assert.Equal(roundedTotal, printed.PayableAmount);
            Assert.Equal(roundingAdjustment, printed.PayableRoundingAmount);
            Assert.Equal(result.Receipt.Lines, printed.Lines);
            Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
            Assert.Equal("FV101", (await fixture.Sales.PreviewNextFiscalNumberAsync(fixture.Scope.DeviceId, fixture.IssuedAt)).FullNumber);
        });
    }

    [Fact]
    public async Task Successful_completion_clears_sale_before_printing_and_previews_the_next_number()
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
            Assert.False(result.PrintedDirectly);
            Assert.Null(result.PrintError);
            Assert.Empty(fixture.Printer.Receipts);
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
    public async Task Completion_and_payment_preserve_the_closed_line_total_without_revalidating_unit_price_times_quantity()
    {
        await WithFixtureAsync(async fixture =>
        {
            var draft = await fixture.Drafts.AddOrIncrementLineAsync(
                fixture.Scope,
                new PosDraftLineInput(
                    new ProductId(Guid.NewGuid()),
                    "P-FRACCION",
                    "Producto fraccionario",
                    "EA",
                    "01",
                    0m,
                    .3m,
                    15_340.45m,
                    15_340.45m,
                    "COP",
                    "Captured",
                    AllowsFractionalSale: true,
                    PublicLineTotal: 4_602.13m));

            var result = await fixture.CompleteAsync(
                draft.DraftId,
                [new OfflineSalePayment("Cash", 4_602.13m,
                    RoundingAdjustment: -2.13m)]);

            Assert.Equal(4_600m, result.IssuedSale.Total);
            var upload = PosSaleContractSerializer.Deserialize(
                Assert.Single(await fixture.Sales.GetPendingOutboxAsync()).Payload);
            var line = Assert.Single(upload.Lines);
            Assert.Equal(.3m, line.Quantity);
            Assert.Equal(4_602.13m, line.LineTotal);
            Assert.NotEqual(
                decimal.Round(.3m * 15_340.45m, 2, MidpointRounding.AwayFromZero),
                line.LineTotal);
            Assert.Equal(4_602.13m, Assert.Single(upload.Payments).Amount);
            Assert.Equal(-2.13m, Assert.Single(upload.Payments).RoundingAdjustment);
            Assert.Equal(4_600m, upload.CommercialSnapshot.PayableAmount);
        });
    }

    [Fact]
    public async Task Print_failure_keeps_issued_sale_and_explicit_reprint_does_not_renumber()
    {
        await WithFixtureAsync(async fixture =>
        {
            var draft = await fixture.AddLineAsync();
            var issued = await fixture.CompleteAsync(draft.DraftId);

            var afterFailure = await fixture.Drafts.GetAsync(draft.DraftId);
            Assert.Equal(PosDraftStatus.Consumed, afterFailure!.Status);
            Assert.False(issued.PrintedDirectly);
            Assert.Null(issued.PrintError);
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

            fixture.Printer.Fail = true;
            await Assert.ThrowsAsync<IOException>(() =>
                new PosSaleCompletionService(
                    fixture.Drafts, fixture.Issuance, fixture.Sales, fixture.Printer)
                    .ReprintAsync(issued.IssuedSale.DocumentId, fixture.Scope.UserId, 80));

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

    [Fact]
    public async Task Below_cost_sale_requires_explicit_permission_and_succeeds_when_authorized()
    {
        await WithFixtureAsync(async fixture =>
        {
            var draft = await fixture.AddLineAsync(documentUnitCost: 10_001m);

            var forbidden = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                fixture.CompleteAsync(draft.DraftId));

            Assert.Contains(CommercePermissionCodes.SalesBelowCost, forbidden.Message);
            Assert.Equal(
                PosDraftStatus.Active,
                (await fixture.Drafts.GetAsync(draft.DraftId))!.Status);
            Assert.Empty(await fixture.Sales.GetPendingOutboxAsync());

            var completed = await fixture.CompleteAsync(
                draft.DraftId,
                permissions: new HashSet<string>(StringComparer.Ordinal)
                {
                    CommercePermissionCodes.SalesBelowCost
                });

            Assert.Equal(PosDraftStatus.Consumed,
                (await fixture.Drafts.GetAsync(draft.DraftId))!.Status);
            Assert.Equal("VTA03-00000100", completed.IssuedSale.DocumentNumber);
            Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
        });
    }

    [Fact]
    public async Task Server_authorized_credit_is_persisted_as_financing_not_received_money()
    {
        await WithFixtureAsync(async fixture =>
        {
            var customerId = Guid.NewGuid();
            var draft = await fixture.AddLineAsync();
            draft = await fixture.Drafts.AssignPartiesAsync(
                draft.DraftId,
                customerId,
                sellerId: null,
                customerPartySiteId: Guid.NewGuid());

            var result = await fixture.CompleteAsync(
                draft.DraftId,
                payments: [],
                credit: new PosSaleCreditTerms(
                    customerId,
                    10_000m,
                    fixture.IssuedAt.AddDays(30),
                    40_000m));

            var pending = Assert.Single(await fixture.Sales.GetPendingOutboxAsync());
            var upload = PosSaleContractSerializer.Deserialize(pending.Payload);
            Assert.Empty(upload.Payments);
            Assert.NotNull(upload.Credit);
            Assert.Equal(customerId, upload.Credit.CustomerId);
            Assert.Equal(10_000m, upload.Credit.Amount);
            Assert.Equal(40_000m, upload.Credit.RemainingCredit);
            var receiptCredit = Assert.Single(result.Receipt.Payments);
            Assert.Equal("Credit", receiptCredit.MethodCode);
            Assert.Equal(10_000m, receiptCredit.Amount);
            Assert.NotNull(result.Receipt.CreditAcknowledgement);
            Assert.Equal(40_000m,
                result.Receipt.CreditAcknowledgement.RemainingCredit);
        });
    }

    [Fact]
    public async Task Server_resolved_credit_due_date_is_preserved_in_the_local_sale_snapshot()
    {
        await WithFixtureAsync(async fixture =>
        {
            var customerId = Guid.NewGuid();
            var draft = await fixture.AddLineAsync();
            draft = await fixture.Drafts.AssignPartiesAsync(
                draft.DraftId,
                customerId,
                sellerId: null,
                customerPartySiteId: Guid.NewGuid());

            var dueDate = fixture.IssuedAt.AddDays(30);
            var result = await fixture.CompleteAsync(
                draft.DraftId,
                payments: [],
                credit: new PosSaleCreditTerms(
                    customerId,
                    10_000m,
                    dueDate));

            Assert.NotNull(result);
            var credit = PosSaleContractSerializer.Deserialize(
                Assert.Single(await fixture.Sales.GetPendingOutboxAsync()).Payload).Credit;
            Assert.NotNull(credit);
            Assert.Equal(customerId, credit.CustomerId);
            Assert.Equal(dueDate, credit.DueDate);
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
            RecordingPrinter printer, string connectionString)
        {
            Scope = scope;
            Register = register;
            Drafts = drafts;
            Issuance = issuance;
            Sales = sales;
            Printer = printer;
            ConnectionString = connectionString;
        }

        public DateTimeOffset IssuedAt { get; } =
            new(2026, 7, 28, 14, 30, 0, TimeSpan.FromHours(-5));
        public string ConnectionString { get; }
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
            return new Fixture(scope, executionContext, drafts, issuance, sales, new RecordingPrinter(), connectionString);
        }

        public Task<PosDraft> AddLineAsync(decimal documentUnitCost = 0m) =>
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
                    "BusinessDefault",
                    DocumentUnitCost: documentUnitCost));

        public Task<CompletePosSaleResult> CompleteAsync(
            DraftId draftId,
            IReadOnlyCollection<OfflineSalePayment>? payments = null,
            PosSaleCreditTerms? credit = null,
            IReadOnlySet<string>? permissions = null) =>
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
                    80,
                    Permissions: permissions,
                    Credit: credit));
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
