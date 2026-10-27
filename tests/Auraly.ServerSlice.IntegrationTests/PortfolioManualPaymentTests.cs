using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Theory]
    [InlineData(false, 10_000, false)]
    [InlineData(true, 10_000, false)]
    [InlineData(false, 4_000, false)]
    [InlineData(true, 4_000, false)]
    [InlineData(false, 10_000, true)]
    [InlineData(true, 10_000, true)]
    public async Task Immediately_due_purchase_is_paid_once_through_the_shared_motor_from_either_capture(
        bool manual, decimal firstAmount, bool correctedBeforeUnapply)
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PurchasingPermissionCodes.ReadGoodsReceipts, PayablesPermissionCodes.RegisterPayment,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.ManualCreate,
            AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Retry, PayablesPermissionCodes.Read,
            WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close, WorkSessionPermissionCodes.ReadCashDifferences,
            WorkSessionPermissionCodes.ReconcileClosures);
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"CONTADO-{Guid.NewGuid():N}", at, at, true, at, "COP", "Compra de contado, pago separado",
            [new(1, productId, "Compra de contado", 1m, 10_000m, 0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)]);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", receipt, receipt.DocumentId.ToString("N"));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        var payableId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);
        var page = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={payableId}&page=1&pageSize=1");
        var position = Assert.Single(page!.Items);
        Assert.Equal(10_000m, position.AvailableAmount);
        Assert.Equal(at.Date, position.DueDate!.Value.Date);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierPaymentApplications WHERE PayableId=@Id", payableId));
        async Task AssertReceiptPayableAsync(decimal outstanding, string status)
        {
            using var reads = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains("SELECT r.DocumentNumber,r.Status", StringComparison.Ordinal)
                    || command.CommandText.Contains("SELECT PayloadJson FROM dbo.DocumentProcessingPayloads", StringComparison.Ordinal)
                    || command.CommandText.Contains("SELECT j.SourceDocumentId,j.SourceDocumentType,j.Status,j.LastErrorCode,j.LastErrorMessage", StringComparison.Ordinal));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var detail = await client.GetFromJsonAsync<GoodsReceiptDetail>($"/api/commerce/v1/goods-receipts/{receipt.DocumentId}");
            elapsed.Stop();
            Assert.NotNull(detail?.Payable);
            Assert.Equal(payableId, detail.Payable.PayableId);
            Assert.Equal(outstanding, detail.Payable.OutstandingAmount);
            Assert.Equal(status, detail.Payable.Status);
            Assert.Equal("COP", detail.Payable.CurrencyCode);
            Assert.Equal(at, detail.Payable.DueDate);
            Assert.NotNull(detail.Payable.PartySiteId);
            Assert.False(string.IsNullOrWhiteSpace(detail.Payable.PartySiteName));
            Assert.Equal(3, reads.Count);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"El detalle de recepción demoró {elapsed.Elapsed.TotalMilliseconds:F1} ms.");
        }
        await AssertReceiptPayableAsync(10_000m, "Open");
        var sessionId = await fixture.OpenWebWorkSessionAsync();
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        async Task<Guid> PayAsync(bool fromVoucher, decimal amount, bool rejected = false)
        {
            var sourceId = Guid.NewGuid();
            var capture = new SaveVoucherDraftRequest(sourceId, AccountingManualDocumentTypes.ManualVoucher, at,
                "MANUAL_VOUCHER", "Pago de compra de contado", "CONTADO",
                [new(position.AccountId, fixture.SupplierPartyId, null, "Cancelar obligación", amount, 0,
                    LineId: Guid.NewGuid(), ExistingPosition: new("Payable", payableId)),
                 new(cash.AccountId, fixture.SupplierPartyId, null, "Salida de caja", 0, amount, LineId: Guid.NewGuid())],
                null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Disbursement", WorkSessionId: sessionId);
            var path = $"/api/commerce/v1/accounting/manual/drafts/{sourceId}";
            VoucherDraftView? saved = null;
            if (fromVoucher)
            {
                using var save = await client.PutAsJsonAsync(path, capture);
                Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
                saved = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            }
            // Browser captures UTC. The same local accounting day can already
            // be tomorrow in UTC; unapplication must compare calendar dates in
            // the voucher's offset at both acceptance and posting.
            var nativePaidAt = at.AddHours(10.5).ToUniversalTime();
            Assert.True(nativePaidAt.Date > at.Date);
            var payment = new ConfirmSupplierPaymentRequest(sourceId, fixture.BusinessId, fixture.SupplierId, nativePaidAt,
                "COP", "Pago de compra de contado", [new(payableId, amount)], [new("Cash", amount, amount)], sessionId);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var response = fromVoucher
                    ? await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved!.RowVersion))
                    : await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment, sourceId.ToString("N"));
                var body = await response.Content.ReadAsStringAsync();
                if (rejected)
                {
                    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                    var detail = JsonNode.Parse(body)!["detail"]!.GetValue<string>();
                    Assert.Contains("El pago no se realizó", detail);
                    Assert.Contains("saldo disponible", detail);
                }
                else Assert.True(response.IsSuccessStatusCode, body);
            }
            Assert.Equal(rejected ? 0 : 1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", sourceId));
            if (rejected)
            {
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", sourceId));
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierPayments WHERE PaymentId=@Id OR ParentSourceDocumentId=@Id", sourceId));
            }
            return sourceId;
        }
        var sources = new List<Guid> { await PayAsync(manual, firstAmount) };
        if (firstAmount < 10_000m)
        {
            Assert.Equal(6_000m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
            await PayAsync(!manual, 7_000m, rejected: true);
            Assert.Equal(6_000m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
            page = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={payableId}&page=1&pageSize=1");
            Assert.Equal(6_000m, Assert.Single(page!.Items).AvailableAmount);
            sources.Add(await PayAsync(!manual, 6_000m));
        }
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal("Paid", await ScalarAsync<string>("SELECT Status FROM dbo.Payables WHERE PayableId=@Id", payableId));
        await AssertReceiptPayableAsync(0m, "Paid");
        Assert.Equal(sources.Count, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE PayableId=@Id AND TransactionType=N'Payment'", payableId));
        Assert.Equal(sources.Count, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierPaymentApplications WHERE PayableId=@Id", payableId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.SupplierPaymentApplications WHERE PayableId=@Id", payableId));
        Assert.Equal(-10_000m, await ScalarAsync<decimal>("""
            SELECT SUM(movement.Amount) FROM dbo.SupplierPaymentApplications application
            JOIN dbo.WorkSessionMovements movement ON movement.DocumentId=application.PaymentId
            WHERE application.PayableId=@Id AND movement.PaymentMethodCode=N'Cash';
            """, payableId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id", receipt.DocumentId));

        // An applied native payment or a group of a manual voucher uses its real
        // transaction identity. Unapplying it must not delete or repeat cash.
        var originalTransaction = await ScalarAsync<Guid>("""
            SELECT TOP(1) PayableTransactionId FROM dbo.PayableTransactions
            WHERE PayableId=@Id AND TransactionType=N'Payment' ORDER BY OccurredAt,PayableTransactionId;
            """, payableId);
        var originalAmount = await ScalarAsync<decimal>("SELECT Amount FROM dbo.PayableTransactions WHERE PayableTransactionId=@Id", originalTransaction);
        var unapplyAmount = 1000.0001m;
        var capturePath = $"/api/commerce/v1/accounting/unapplication-capture?subledgerKind=Payable&subledgerId={payableId}&transactionId={originalTransaction}";
        using (var reads = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("SELECT opening.AccountId,account.Code,account.Name", StringComparison.Ordinal)))
        {
            var proposal = (await client.GetFromJsonAsync<VoucherDraftPreparation>(capturePath))!;
            Assert.Equal(1, reads.Count);
            Assert.Equal(fixture.SupplierPartyId, proposal.MainPartyId);
            Assert.Equal(originalAmount, proposal.Lines[0].Value.Credit);
            Assert.Equal(position.AccountId, proposal.Lines[0].Value.AccountId);
            Assert.Equal(originalTransaction, proposal.Lines[0].Value.ExistingPosition!.ReversesTransactionId);
            Assert.Null(proposal.Lines[1].Value.AccountId);
            Assert.Equal(originalAmount, proposal.Lines[1].Value.Debit);
        }
        using (var denied = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate))
        using (var response = await denied.GetAsync(capturePath))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await client.GetAsync(capturePath.Replace(originalTransaction.ToString(), Guid.NewGuid().ToString())))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "1396", "Asset", "SupplierAdvance", "Debit");
        async Task<VoucherDraftView> SaveUnapplicationAsync(decimal amount)
        {
            var id = Guid.NewGuid();
            var capture = new SaveVoucherDraftRequest(id, AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1),
                "MANUAL_VOUCHER", "Desaplicar conservando el dinero real", "DESAPLICAR",
                [new(advance.AccountId, fixture.SupplierPartyId, null, "Anticipo conservado", amount, 0, LineId: Guid.NewGuid()),
                 new(position.AccountId, fixture.SupplierPartyId, null, "Reabrir obligación", 0, amount, LineId: Guid.NewGuid(),
                    ExistingPosition: new("Payable", payableId, originalTransaction))],
                null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
            using var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{id}", capture);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal(originalTransaction, saved.Lines[1].Value.ExistingPosition!.ReversesTransactionId);
            return saved;
        }
        async Task<(WorkSessionClosureView Closure, string Path, WorkSessionPaymentVerificationItem Payment,
            IReadOnlyList<WorkSessionPaymentVerificationItem> Verifications)> CloseOriginalPaymentAsync()
        {
            WorkSessionClosureView closure;
            using (var response = await SendAsync(client, $"/api/commerce/v1/work-sessions/{sessionId}/close",
                new CloseWorkSessionRequest(0m, "Verificar dependencias de la desaplicación"), $"close-unapply-{Guid.NewGuid():N}"))
            {
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                closure = (await response.Content.ReadFromJsonAsync<WorkSessionClosureView>())!;
            }
            var originalPaymentId = await ScalarAsync<Guid>("SELECT SourceDocumentId FROM dbo.PayableTransactions WHERE PayableTransactionId=@Id", originalTransaction);
            var originalPaymentNumber = await ScalarAsync<string>("SELECT DocumentNumber FROM accounting.PortfolioPaymentOrigins WHERE PaymentId=@Id AND SubledgerKind=N'Payable'", originalPaymentId);
            var originalMovementId = await ScalarAsync<Guid>("SELECT WorkSessionMovementId FROM dbo.WorkSessionMovements WHERE DocumentId=@Id AND MovementType=N'PayablePayment'", originalPaymentId);
            var path = $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId}";
            var verifications = await client.GetFromJsonAsync<WorkSessionPaymentVerificationItem[]>($"{path}/payment-verifications");
            var payment = Assert.Single(verifications!, item => item.SourceId == originalMovementId || item.SourceId == originalPaymentId);
            Assert.Equal(originalPaymentNumber, payment.DocumentNumber);
            return (closure, path, payment, verifications!);
        }
        if (correctedBeforeUnapply)
        {
            var closed = await CloseOriginalPaymentAsync();
            var request = new ReconcileWorkSessionClosureRequest(closed.Closure.PaymentTotals.Where(item => item.RequiresCount)
                .Select(item => new ReconcileWorkSessionClosureLine(item.PaymentMethodCode, 0m, true,
                    item.PaymentMethodCode == "Cash" ? "COUNT_DIFFERENCE" : null)).ToArray(),
                [], "Revisión del importe pagado", closed.Verifications.Select(item =>
                    new WorkSessionPaymentVerificationDecision(item.VerificationKey, "Verified")).ToArray(),
                [new(closed.Payment.VerificationKey, "Cash", closed.Payment.Amount + 1m, "Importe real corregido")]);
            using (var response = await SendAsync(client, closed.Path + "/reconcile", request, $"reconcile-unapply-{Guid.NewGuid():N}"))
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal(1m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
            var blocked = await SaveUnapplicationAsync(unapplyAmount);
            using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{blocked.DocumentId}/send", new SendVoucherDraftRequest(blocked.RowVersion)))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains("su importe fue corregido", await response.Content.ReadAsStringAsync());
            }
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", blocked.DocumentId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id", blocked.DocumentId));
            Assert.Equal(1m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
            return;
        }
        var unapplication = await SaveUnapplicationAsync(unapplyAmount);
        var unapplyPath = $"/api/commerce/v1/accounting/manual/drafts/{unapplication.DocumentId}";
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
            beforeExecute: command =>
            {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                var advanceLine = journal.Single(item => item!["SourceLineId"]!.GetValue<Guid>() == unapplication.Lines[0].Value.LineId);
                advanceLine!["SourceLineId"] = Guid.NewGuid();
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var response = await client.PostAsJsonAsync(unapplyPath + "/send", new SendVoucherDraftRequest(unapplication.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(1, fault.Count);
        }
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id", unapplication.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id", unapplication.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", unapplication.DocumentId));
        var reservedProposal = (await client.GetFromJsonAsync<VoucherDraftPreparation>(capturePath))!;
        Assert.Equal(originalAmount - unapplyAmount, reservedProposal.Lines[0].Value.Credit);
        Assert.Equal(originalAmount - unapplyAmount, reservedProposal.Lines[1].Value.Debit);
        var pendingExcess = await SaveUnapplicationAsync(originalAmount);
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{pendingExcess.DocumentId}/send", new SendVoucherDraftRequest(pendingExcess.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("pendientes", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", pendingExcess.DocumentId));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsync($"/api/commerce/v1/accounting/postings/{unapplication.DocumentId}/retry", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsJsonAsync(unapplyPath + "/send", new SendVoucherDraftRequest(unapplication.RowVersion));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        var unapplied = (await client.GetFromJsonAsync<VoucherDraftView>(unapplyPath))!;
        Assert.Equal("Posted", unapplied.Status);
        var preservedAdvance = Assert.Single(unapplied.FinancialEffects!.Positions);
        var reversal = Assert.Single(unapplied.FinancialEffects.Unapplications!);
        Assert.Equal(originalTransaction, reversal.ReversesTransactionId);
        Assert.Equal(preservedAdvance.PositionId, reversal.AdvancePositionId);
        Assert.Empty(unapplied.FinancialEffects.Payments);
        Assert.Empty(unapplied.FinancialEffects.DirectMovements);
        await AssertReceiptPayableAsync(unapplyAmount, "PartiallyPaid");
        Assert.Equal(unapplyAmount, await ScalarAsync<decimal>("SELECT AvailableAmount FROM dbo.SupplierCredits WHERE SupplierCreditId=@Id", preservedAdvance.PositionId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Unapplication'", unapplication.DocumentId));
        Assert.Equal(originalTransaction, await ScalarAsync<Guid>("SELECT ReversesTransactionId FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id", unapplication.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", unapplication.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", unapplication.DocumentId));
        var detailAfterUnapply = (await client.GetFromJsonAsync<PayableDetail>($"/api/commerce/v1/payables/{payableId}"))!;
        Assert.Equal(unapplyAmount, Assert.Single(detailAfterUnapply.Transactions, item => item.Type == "Unapplication").BalanceChange);
        var paidPage = await client.GetFromJsonAsync<PayablePage>(
            $"/api/commerce/v1/payables?search={Uri.EscapeDataString(position.DocumentNumber)}&page=1&pageSize=20");
        Assert.Equal(10_000m - unapplyAmount, Assert.Single(paidPage!.Items, item => item.PayableId == payableId).PaidAmount);
        var supplierPage = await client.GetFromJsonAsync<SupplierPortfolioPage>(
            $"/api/commerce/v1/payables/suppliers?search={Uri.EscapeDataString(position.DocumentNumber)}&page=1&pageSize=20");
        Assert.Equal(10_000m - unapplyAmount, Assert.Single(supplierPage!.Items).PaidAmount);
        var report = await client.GetFromJsonAsync<PayablesReportPage>(
            $"/api/commerce/v1/payables/report?search={Uri.EscapeDataString(position.DocumentNumber)}&page=1&pageSize=20&consolidated=false&cutoff=2026-12-31");
        var reportItem = Assert.Single(report!.Items, item => item.PayableId == payableId);
        Assert.Equal(10_000m - unapplyAmount, reportItem.PaidAmount);
        Assert.NotNull(reportItem.Applications);
        Assert.Equal(reportItem.PaidAmount, reportItem.Applications.Sum(item => item.Amount));
        Assert.Contains(reportItem.Applications, item => item.Amount == -unapplyAmount && item.DocumentNumber.StartsWith("Desaplicación de "));

        var excessive = await SaveUnapplicationAsync(originalAmount);
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{excessive.DocumentId}/send", new SendVoucherDraftRequest(excessive.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("No se desaplicó", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", excessive.DocumentId));
        Assert.Equal(unapplyAmount, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(-10_000m, await ScalarAsync<decimal>("""
            SELECT SUM(movement.Amount) FROM dbo.SupplierPaymentApplications application
            JOIN dbo.WorkSessionMovements movement ON movement.DocumentId=application.PaymentId
            WHERE application.PayableId=@Id AND movement.PaymentMethodCode=N'Cash';
            """, payableId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.SupplierPaymentApplications WHERE PayableId=@Id", payableId));
        var closedAfter = await CloseOriginalPaymentAsync();
        using (var response = await client.PostAsJsonAsync($"{closedAfter.Path}/payment-corrections/validate",
            new[] { new WorkSessionPaymentCorrection(closedAfter.Payment.VerificationKey, "Cash", closedAfter.Payment.Amount - 1m,
                "No modificar un pago ya desaplicado") }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("desaplicaciones", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Manual_payment_groups_share_the_parent_and_apply_one_position_atomically_without_child_jobs_or_numbers()
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.RegisterPayment, PayablesPermissionCodes.Read,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.ManualCreate,
            AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Retry, WorkSessionPermissionCodes.Read);
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"MANUAL-{Guid.NewGuid():N}", at, at, true, at.AddDays(30), "COP", null,
            [new(1, productId, "Compra para pagar desde cuentas", 1m, 10_000m, 0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)]);
        using (var response = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", receipt, receipt.DocumentId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var payableId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);
        var page = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={payableId}&page=1&pageSize=10");
        var position = Assert.Single(page!.Items);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var session = await fixture.OpenWorkSessionAsync(client);
        var documentId = Guid.NewGuid();
        var lines = new List<VoucherDraftLine>();
        foreach (var amount in new[] { 100m, 200m })
        {
            var group = Guid.NewGuid();
            lines.Add(new(position.AccountId, fixture.SupplierPartyId, null, "Abono a la compra", amount, 0,
                LineId: Guid.NewGuid(), ExistingPosition: new("Payable", payableId), FinancialGroupId: group));
            lines.Add(new(cash.AccountId, null, null, "Salida de caja", 0, amount, LineId: Guid.NewGuid(),
                FinancialGroupId: group, PaymentEvidence: new(TenderedAmount: amount)));
        }
        var capture = new SaveVoucherDraftRequest(documentId, AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1),
            "MANUAL_VOUCHER", "Un comprobante con dos abonos", "MAN-GROUPS", lines, null, null,
            MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Disbursement", WorkSessionId: session.WorkSessionId);
        var unclassified = new List<Guid>();
        foreach (var definition in new[] { (Prefix: "51", Type: "Expense"), (Prefix: "23", Type: "Liability") })
        {
            var accountId = Guid.NewGuid();
            var code = definition.Prefix + BitConverter.ToUInt64(accountId.ToByteArray()).ToString("D20");
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
                new CreateAccountingAccountRequest(accountId, fixture.TenantId, code, "Auxiliar pendiente de clasificación", definition.Type, true, false));
            response.EnsureSuccessStatusCode();
            unclassified.Add(accountId);
        }
        var unclassifiedId = Guid.NewGuid();
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{unclassifiedId}", capture with
        {
            DocumentId = unclassifiedId, VoucherTypeCode = "Accounting", WorkSessionId = null,
            Lines = [new(unclassified[0], fixture.SupplierPartyId, null, "Cuenta por clasificar", 100, 0, LineId: Guid.NewGuid()),
                new(unclassified[1], fixture.SupplierPartyId, null, "Contrapartida por clasificar", 0, 100, LineId: Guid.NewGuid())]
        }))
        {
            response.EnsureSuccessStatusCode();
            var incomplete = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var rejected = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{unclassifiedId}/send", new SendVoucherDraftRequest(incomplete.RowVersion));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("No se infiere por su código o nombre", await rejected.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", unclassifiedId));
        VoucherDraftView saved;
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}", capture))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        using (var denied = fixture.CreateAdminClient(AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Read))
        using (var response = await denied.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", documentId));

        foreach (var invalid in new[] { "date", "parent", "hash", "evidence", "allocation", "site" })
        {
            using var fault = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText == "accounting.PortfolioPaymentAccept",
                beforeExecute: command =>
                {
                    if (invalid == "hash")
                    {
                        command.Parameters["@PayloadHash"].Value = new byte[32];
                        return;
                    }
                    var payload = JsonNode.Parse((string)command.Parameters["@Payload"].Value)!;
                    var group = payload["groups"]![1]!;
                    if (invalid == "date") group["paidAt"] = at.AddDays(2).ToString("O");
                    else if (invalid == "parent") group["parentSourceDocumentId"] = Guid.NewGuid();
                    else if (invalid == "evidence") group["payments"]![0]!["approvalNumber"] = "NOT-IN-PARENT";
                    else if (invalid == "site") group["partySiteId"] = Guid.NewGuid();
                    else
                    {
                        group["totalAmount"] = 201m;
                        group["allocations"]![0]!["amount"] = 201m;
                        group["payments"]![0]!["amount"] = 201m;
                        group["payments"]![0]!["tenderedAmount"] = 201m;
                    }
                    command.Parameters["@Payload"].Value = payload.ToJsonString();
                });
            using var rejected = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}/send", new SendVoucherDraftRequest(saved.RowVersion));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            if (invalid is "evidence" or "allocation" or "site")
            {
                var problem = (await rejected.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("detail").GetString();
                Assert.Contains("El pago no se realizó", problem);
                Assert.Contains("comprobante padre", problem);
            }
            Assert.Equal(1, fault.Count);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", documentId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id", documentId));
        }

        VoucherDraftView sent;
        using (var writes = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture))
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            sent = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", sent.Status);
            Assert.Equal(1, writes.Count);
        }
        var payments = sent.FinancialEffects!.Payments;
        Assert.Equal(2, payments.Count);
        Assert.Equal(9_700m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var state = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id AND Status=N'Processed' AND DocumentNumber IS NULL),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId IN(SELECT PaymentId FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id)),
              (SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId IN(SELECT PaymentId FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id)),
              (SELECT COUNT(*) FROM dbo.PayableTransactions WHERE PayableId=@Position AND TransactionType=N'Payment'
                AND SourceDocumentId IN(SELECT PaymentId FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id));
            """, connection);
        state.Parameters.AddWithValue("@Id", documentId);
        state.Parameters.AddWithValue("@Position", payableId);
        await using (var reader = await state.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(new[] { 2, 1, 1, 0, 2, 2 }, Enumerable.Range(0, 6).Select(reader.GetInt32));
        }
        var history = await client.GetFromJsonAsync<SupplierPaymentHistoryPage>(
            $"/api/commerce/v1/payable-payments?supplierId={fixture.SupplierId}&search={Uri.EscapeDataString(sent.Row.EntryNumber!)}&page=1&pageSize=10");
        Assert.Equal(2, history!.TotalCount);
        Assert.All(history.Items, item => Assert.Equal(sent.Row.EntryNumber, item.DocumentNumber));
        Assert.All(history.Items, item =>
        {
            Assert.Equal(documentId, item.SourceDocumentId);
            Assert.Equal(AccountingManualDocumentTypes.ManualVoucher, item.SourceDocumentType);
            Assert.Contains(item.SourceGroupId, payments.Select(payment => (Guid?)payment.GroupId));
        });
        Assert.Equal(300m, history.Items.Sum(item => item.TotalAmount));
        var detail = await client.GetFromJsonAsync<PayableDetail>($"/api/commerce/v1/payables/{payableId}");
        Assert.Equal(2, detail!.Transactions.Count(item => item.Type == "Payment" && item.SourceDocumentId == documentId));
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var replay = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal(payments.Select(item => item.PaymentId), replay.FinancialEffects!.Payments.Select(item => item.PaymentId));
        }
        Assert.Equal(9_700m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        var collision = new ConfirmSupplierPaymentRequest(payments[0].PaymentId, fixture.BusinessId, fixture.SupplierId,
            at.AddDays(1), "COP", null, [new(payableId, 100m)], [new("Cash", 100m, 100m)]);
        using (var response = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", collision, collision.PaymentId.ToString("N")))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("pertenece a un comprobante", await response.Content.ReadAsStringAsync());
        }

        // Fail after balances/applications/history have been updated inside the
        // participant. Its caller must roll back every group, retaining only the
        // immutable acceptance and its single set of pending reservations.
        var failedId = Guid.NewGuid();
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{failedId}",
            capture with { DocumentId = failedId, Lines = lines.Select(line => line with { LineId = Guid.NewGuid() }).ToArray() }))
        {
            response.EnsureSuccessStatusCode();
            saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("DECLARE @Payments TABLE", StringComparison.Ordinal)
                && command.Parameters["@Rows"].Value is string json && json.Contains(failedId.ToString(), StringComparison.OrdinalIgnoreCase),
            beforeExecute: command =>
            {
                var rows = JsonNode.Parse((string)command.Parameters["@Rows"].Value)!.AsArray();
                var tender = rows[0]!["Tenders"]![0]!;
                tender["Amount"] = tender["Amount"]!.GetValue<decimal>() + 1m;
                command.Parameters["@Rows"].Value = rows.ToJsonString();
            }))
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{failedId}/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(1, fault.Count);
        }
        Assert.Equal(9_700m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", failedId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId IN(SELECT PaymentId FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id)", failedId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierPaymentApplications WHERE AppliedAt IS NOT NULL AND PaymentId IN(SELECT PaymentId FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id)", failedId));
        var pending = await client.GetFromJsonAsync<VoucherDraftView>($"/api/commerce/v1/accounting/manual/drafts/{failedId}");
        Assert.Equal("TechnicalProcessingFailure", pending!.Row.ErrorCode);
        Assert.Contains("no se aplicó", pending.Row.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        page = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={payableId}&page=1&pageSize=10");
        Assert.Equal(9_400m, Assert.Single(page!.Items).AvailableAmount);
        var overAvailable = collision with { PaymentId = Guid.NewGuid(), Allocations = [new(payableId, 9_500m)], Payments = [new("Cash", 9_500m, 9_500m)] };
        using (var response = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", overAvailable, overAvailable.PaymentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{failedId}/send", new SendVoucherDraftRequest(saved.RowVersion)))
            response.EnsureSuccessStatusCode();
        Assert.Equal(9_700m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsync($"/api/commerce/v1/accounting/postings/{failedId}/retry", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(9_400m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", failedId));
    }
}
