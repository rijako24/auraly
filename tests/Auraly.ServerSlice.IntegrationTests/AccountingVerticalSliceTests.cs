using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Contracts.Dispatching;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountingSliceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly accounting slice";
}

[Collection(AccountingSliceCollection.Name)]
[Trait("EngineCertification", "Accounting")]
public sealed class AccountingVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Dispatch_cash_shortage_posts_once_to_the_configured_account()
    {
        using var accounting = fixture.CreateAdminClient(
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, DispatchPermissionCodes.Settle,
            DispatchPermissionCodes.ReadAll);
        using (var defaults = await accounting.PutAsync(
                   "/api/commerce/v1/accounting/defaults", null))
            defaults.EnsureSuccessStatusCode();
        using (var activate = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/activate",
                   new ActivateAccountingRequest(
                       new DateOnly(2026, 1, 1), "COP", "ZeroDeclared")))
            activate.EnsureSuccessStatusCode();

        var invoice = WithUblSnapshot(fixture.CreateValidRequest(9_899));
        await SetWarehouseNegativeSalesPolicyAsync(false);
        try
        {
            using var upload = fixture.CreateUploadMessage(invoice);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await SetWarehouseNegativeSalesPolicyAsync(true);
        }

        var dispatchId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var settlementId = Guid.NewGuid();
        var originalShortageAccount = await SeedDispatchSettlementAsync(
            dispatchId, sourceId, settlementId, invoice.DocumentId,
            invoice.CommercialSnapshot.PayableAmount);
        var settlementWorker = ActivatorUtilities.CreateInstance<DispatchSettlementHostedService>(
            fixture.Services);
        await settlementWorker.StartAsync(CancellationToken.None);
        try
        {
            var request = new SettleDispatchRequest(
                invoice.CommercialSnapshot.PayableAmount - 1_000m,
                "Faltante entregado por el transportador", $"settle-{dispatchId:N}");
            using var settle = await accounting.PostAsJsonAsync(
                $"/api/commerce/v1/dispatches/{dispatchId:D}/settle", request);
            Assert.True(settle.IsSuccessStatusCode,
                await settle.Content.ReadAsStringAsync());

            await WaitForDispatchAccountingAsync(settlementId, dispatchId);

            await AssertBalancedAsync(settlementId);
            Assert.Equal(1_000m, await AccountAmountAsync(
                settlementId, "539595", debit: true));
            Assert.Equal(1_000m, await AccountAmountAsync(
                settlementId, "110505", debit: false));
            Assert.Equal("Closed", await ScalarAsync<string>(
                "SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id", dispatchId));

            using var replay = await accounting.PostAsJsonAsync(
                $"/api/commerce/v1/dispatches/{dispatchId:D}/settle", request);
            Assert.True(replay.IsSuccessStatusCode,
                await replay.Content.ReadAsStringAsync());
            Assert.Equal(1, await CountAsync(
                "AccountingEntries", "SourceDocumentId", settlementId));
        }
        finally
        {
            await settlementWorker.StopAsync(CancellationToken.None);
            settlementWorker.Dispose();
            await RestoreShortageAccountAsync(originalShortageAccount);
        }
    }

    [Fact]
    public async Task Pos_mixed_payment_methods_are_all_posted_to_their_configured_accounts()
    {
        using var accounting = fixture.CreateAdminClient(
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate);
        using (var defaults = await accounting.PutAsync(
                   "/api/commerce/v1/accounting/defaults", null))
            defaults.EnsureSuccessStatusCode();
        using (var activate = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/activate",
                   new ActivateAccountingRequest(
                       new DateOnly(2026, 1, 1), "COP", "ZeroDeclared")))
            activate.EnsureSuccessStatusCode();

        var customerId = await CreateCustomerAsync();
        var request = WithUblSnapshot(fixture.CreateValidRequest(9_900)) with
        {
            CustomerId = customerId,
            Credit = new PosSaleCreditContract(
                customerId, 4_000m,
                new DateTimeOffset(2026, 8, 31, 0, 0, 0,
                    TimeSpan.FromHours(-5))),
            Payments =
            [
                new(1, "Cash", 1_900m, null),
                new(2, "DebitCard", 2_000m, "DB-2000", "Visa", "APP-DB"),
                new(3, "CreditCard", 2_000m, "CR-2000", "Mastercard", "APP-CR"),
                new(4, "Transfer", 2_000m, "TR-2000")
            ]
        };
        request = request with
        {
            UblSnapshot = request.UblSnapshot! with
            {
                PaymentFormCode = "2",
                DueDate = new DateOnly(2026, 8, 31)
            }
        };
        await SetWarehouseNegativeSalesPolicyAsync(false);
        try
        {
            using var upload = fixture.CreateUploadMessage(request);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await SetWarehouseNegativeSalesPolicyAsync(true);
        }

        await AssertBalancedAsync(request.DocumentId);
        Assert.Equal(1_900m, await AccountAmountAsync(request.DocumentId, "110505", true));
        Assert.Equal(2_000m, await AccountAmountAsync(request.DocumentId, "130510", true));
        Assert.Equal(2_000m, await AccountAmountAsync(request.DocumentId, "130515", true));
        Assert.Equal(2_000m, await AccountAmountAsync(request.DocumentId, "130520", true));
        Assert.Equal(4_000m, await AccountAmountAsync(request.DocumentId, "130505", true));
    }

    [Fact]
    public async Task Approved_opening_balances_are_posted_before_accounting_becomes_ready()
    {
        await DisableAccountingAsync();
        using var accounting = fixture.CreateAdminClient(
            AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate);
        using (var defaults = await accounting.PutAsync("/api/commerce/v1/accounting/defaults", null))
            defaults.EnsureSuccessStatusCode();

        var effectiveOn = new DateOnly(2026, 3, 1);
        using (var missing = await accounting.GetAsync(
                   $"/api/commerce/v1/accounting/readiness?effectiveFrom={effectiveOn:yyyy-MM-dd}&openingBalanceMode=ImportedAndApproved"))
        {
            missing.EnsureSuccessStatusCode();
            var readiness = await missing.Content.ReadFromJsonAsync<AccountingReadinessView>();
            Assert.Contains(readiness!.BlockingIssues,
                issue => issue.Contains("saldos iniciales aprobados", StringComparison.Ordinal));
        }

        using var accountsResponse = await accounting.GetAsync("/api/commerce/v1/accounting/accounts");
        accountsResponse.EnsureSuccessStatusCode();
        var accounts = await accountsResponse.Content.ReadFromJsonAsync<AccountingAccountView[]>() ?? [];
        var debitAccount = accounts.First(account => account.IsActive && account.AllowsPosting && account.AccountType == "Asset" && !account.RequiresParty);
        var creditAccount = accounts.First(account => account.IsActive && account.AllowsPosting && account.AccountType == "Equity" && !account.RequiresParty);
        var batchId = Guid.NewGuid();
        var request = new SaveAccountingOpeningBalanceRequest(
            batchId, fixture.BusinessId, effectiveOn, "COP", "Asiento de apertura certificado", null,
            [
                new(debitAccount.AccountId, null, null, "Disponible inicial", 125_000m, 0m),
                new(creditAccount.AccountId, null, null, "Patrimonio inicial", 0m, 125_000m)
            ]);
        using (var save = await accounting.PutAsJsonAsync(
                   "/api/commerce/v1/accounting/opening-balances", request))
        {
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            Assert.Equal(AccountingOpeningBalanceStatuses.Draft,
                (await save.Content.ReadFromJsonAsync<AccountingOpeningBalanceView>())!.Status);
        }
        using (var approve = await accounting.PostAsJsonAsync(
                   $"/api/commerce/v1/accounting/opening-balances/{batchId:D}/approve", new { }))
        {
            approve.EnsureSuccessStatusCode();
            Assert.Equal(AccountingOpeningBalanceStatuses.Approved,
                (await approve.Content.ReadFromJsonAsync<AccountingOpeningBalanceView>())!.Status);
        }
        using (var activate = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/activate",
                   new ActivateAccountingRequest(effectiveOn, "COP", "ImportedAndApproved")))
        {
            activate.EnsureSuccessStatusCode();
            var readiness = await activate.Content.ReadFromJsonAsync<AccountingReadinessView>();
            Assert.Equal(AccountingActivationStatuses.Ready, readiness!.Status);
            Assert.Empty(readiness.BlockingIssues);
        }
        using var entryResponse = await accounting.GetAsync(
            $"/api/commerce/v1/accounting/entries/by-document/{batchId:D}");
        entryResponse.EnsureSuccessStatusCode();
        var entry = await entryResponse.Content.ReadFromJsonAsync<AccountingEntryView>();
        Assert.Equal(AccountingManualDocumentTypes.OpeningBalance, entry!.SourceDocumentType);
        Assert.Equal(125_000m, entry.DebitTotal);
        Assert.Equal(entry.DebitTotal, entry.CreditTotal);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    [Trait("EngineCertification", "EndToEnd")]
    public async Task Invoice_and_credit_note_post_balanced_once_and_periods_are_controlled()
    {
        await DisableAccountingAsync();
        var unconfiguredInvoice = WithUblSnapshot(fixture.CreateValidRequest(9_811));
        await SetWarehouseNegativeSalesPolicyAsync(false);
        try
        {
            using var pos = fixture.CreateClient();
            using var upload = fixture.CreateUploadMessage(unconfiguredInvoice);
            using var response = await pos.SendAsync(upload);
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await SetWarehouseNegativeSalesPolicyAsync(true);
        }

        Assert.Equal(0, await CountAsync(
            "AccountingPostingJobs", "SourceDocumentId", unconfiguredInvoice.DocumentId));
        await AssertFastProcessingAsync(
            unconfiguredInvoice.DocumentId, "venta sin contabilidad activa");
        Assert.Equal(0, await CountAsync(
            "AccountingEntries", "SourceDocumentId", unconfiguredInvoice.DocumentId));

        using var accounting = fixture.CreateAdminClient(
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.PeriodsManage, AccountingPermissionCodes.Retry,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate,
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PurchasingPermissionCodes.ReadPurchaseReturns,
            PurchasingPermissionCodes.CreatePurchaseReturns,
            PurchasingPermissionCodes.ConfirmPurchaseReturns,
            TaxationPermissionCodes.ViewWithholdingRules,
            TaxationPermissionCodes.ManageWithholdingRules,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Open,
            WorkSessionPermissionCodes.Close,
            WorkSessionPermissionCodes.ManageCash,
            WorkSessionPermissionCodes.ReadCashDifferences);
        // Expense permissions exercise the complete concept -> expense -> AP -> accounting flow below.
        using (var defaultsResponse = await accounting.PutAsync(
                   "/api/commerce/v1/accounting/defaults", null))
        {
            defaultsResponse.EnsureSuccessStatusCode();
            var defaults = await defaultsResponse.Content
                .ReadFromJsonAsync<AccountingDefaultsResult>()
                ?? throw new InvalidOperationException(
                    "The accounting defaults response is empty.");
            Assert.True(defaults.IsReady);
            Assert.True(defaults.AccountCount >= 43);
            Assert.Equal(51, defaults.MappingCount);
            Assert.True(defaults.HasDefaultCostCenter);
            Assert.True(defaults.HasOpenPeriod);
        }

        using (var activateResponse = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/activate",
                   new ActivateAccountingRequest(
                       new DateOnly(2026, 1, 1), "COP", "ZeroDeclared")))
        {
            activateResponse.EnsureSuccessStatusCode();
            var readiness = await activateResponse.Content
                .ReadFromJsonAsync<AccountingReadinessView>();
            Assert.NotNull(readiness);
            Assert.Equal(AccountingActivationStatuses.Ready, readiness.Status);
            Assert.Empty(readiness.BlockingIssues);
        }

        var invoice = WithUblSnapshot(fixture.CreateValidRequest(9_812));
        await SetWarehouseNegativeSalesPolicyAsync(false);
        try
        {
            using var pos = fixture.CreateClient();
            using var upload = fixture.CreateUploadMessage(invoice);
            using var response = await pos.SendAsync(upload);
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await SetWarehouseNegativeSalesPolicyAsync(true);
        }
        await AssertBalancedAsync(invoice.DocumentId);


        var cashierId = await CreateCashierAsync();
        using (var cashier = fixture.CreateUserClient(
                   cashierId,
                   WorkSessionPermissionCodes.Read,
                   WorkSessionPermissionCodes.Open,
                   WorkSessionPermissionCodes.Close,
                   WorkSessionPermissionCodes.ManageCash,
                   WorkSessionPermissionCodes.ReadCashDifferences))
        {
            using var openResponse = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/work-sessions/current",
                new OpenWorkSessionRequest(
                    fixture.BusinessId,
                    fixture.WarehouseId,
                    null,
                    10_000m));
            openResponse.EnsureSuccessStatusCode();
            var session = await openResponse.Content.ReadFromJsonAsync<WorkSessionView>()
                ?? throw new InvalidOperationException("The work session response is empty.");

            using var reasonsResponse = await cashier.GetAsync(
                $"/api/commerce/v1/work-sessions/cash-reasons?businessId={fixture.BusinessId:D}&direction=In");
            reasonsResponse.EnsureSuccessStatusCode();
            var reasons = await reasonsResponse.Content.ReadFromJsonAsync<CashMovementReasonView[]>()
                ?? [];
            var reason = Assert.Single(reasons, item => item.Code == "OTHER_INCOME");
            Assert.True(reason.IsAccountingConfigured);

            var cashDocumentId = Guid.NewGuid();
            using var cashRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/commerce/v1/work-sessions/{session.WorkSessionId:D}/cash-movements")
            {
                Content = JsonContent.Create(new ConfirmCashMovementRequest(
                    cashDocumentId, fixture.BusinessId, session.WorkSessionId,
                    reason.ReasonId, 5_000m,
                    new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(-5)),
                    "Ingreso de prueba", null, null))
            };
            cashRequest.Headers.Add("Idempotency-Key", $"cash-{cashDocumentId:N}");
            using var cashResponse = await cashier.SendAsync(cashRequest);
            Assert.Equal(HttpStatusCode.Accepted, cashResponse.StatusCode);
            await AssertBalancedAsync(cashDocumentId);
            await AssertFastProcessingAsync(cashDocumentId, "movimiento de caja");
            Assert.Equal(5_000m, await AccountAmountAsync(cashDocumentId, "110505", debit: true));
            Assert.Equal(5_000m, await AccountAmountAsync(cashDocumentId, "429595", debit: false));

            using var closeRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/commerce/v1/work-sessions/{session.WorkSessionId:D}/close")
            {
                Content = JsonContent.Create(
                    new CloseWorkSessionRequest(14_000m, "Faltante nocturno verificado"))
            };
            closeRequest.Headers.Add("Idempotency-Key", $"close-{session.WorkSessionId:N}");
            using var closeResponse = await cashier.SendAsync(closeRequest);
            Assert.True(closeResponse.IsSuccessStatusCode,
                await closeResponse.Content.ReadAsStringAsync());
            var closure = await closeResponse.Content.ReadFromJsonAsync<WorkSessionClosureView>()
                ?? throw new InvalidOperationException("The work session closure is empty.");
            Assert.Equal(15_000m, closure.TotalOther);
            Assert.Equal(15_000m, closure.ExpectedCash);
            Assert.Equal(14_000m, closure.CountedCash);
            Assert.Equal(-1_000m, closure.CashDifference);
            await AssertBalancedAsync(closure.WorkSessionClosureId);
            Assert.Equal(1_000m, await AccountAmountAsync(
                closure.WorkSessionClosureId, "139995", debit: true));
            Assert.Equal(1_000m, await AccountAmountAsync(
                closure.WorkSessionClosureId, "110505", debit: false));

            using var secondOpenResponse = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/work-sessions/current",
                new OpenWorkSessionRequest(
                    fixture.BusinessId, fixture.WarehouseId, null, 10_000m));
            secondOpenResponse.EnsureSuccessStatusCode();
            var secondSession = await secondOpenResponse.Content
                .ReadFromJsonAsync<WorkSessionView>()
                ?? throw new InvalidOperationException("The second work session response is empty.");
            using var secondCloseRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/commerce/v1/work-sessions/{secondSession.WorkSessionId:D}/close")
            {
                Content = JsonContent.Create(
                    new CloseWorkSessionRequest(11_000m, "Sobrante nocturno verificado"))
            };
            secondCloseRequest.Headers.Add(
                "Idempotency-Key", $"close-{secondSession.WorkSessionId:N}");
            using var secondCloseResponse = await cashier.SendAsync(secondCloseRequest);
            secondCloseResponse.EnsureSuccessStatusCode();
            var secondClosure = await secondCloseResponse.Content
                .ReadFromJsonAsync<WorkSessionClosureView>()
                ?? throw new InvalidOperationException("The second work-session closure is empty.");
            Assert.Equal(1_000m, secondClosure.CashDifference);
            await AssertBalancedAsync(secondClosure.WorkSessionClosureId);
            Assert.Equal(1_000m, await AccountAmountAsync(
                secondClosure.WorkSessionClosureId, "110505", debit: true));
            Assert.Equal(1_000m, await AccountAmountAsync(
                secondClosure.WorkSessionClosureId, "139995", debit: false));

            using var differencesResponse = await cashier.GetAsync(
                "/api/commerce/v1/work-sessions/cash-differences?from=2026-01-01&to=2026-12-31");
            differencesResponse.EnsureSuccessStatusCode();
            var differences = await differencesResponse.Content
                .ReadFromJsonAsync<WorkSessionCashDifferenceView[]>() ?? [];
            Assert.Contains(differences, value =>
                value.WorkSessionClosureId == closure.WorkSessionClosureId &&
                value.Treatment == "ShortageExpense" &&
                value.AccountingStatus == AccountingPostingStatuses.Posted);
            Assert.Contains(differences, value =>
                value.WorkSessionClosureId == secondClosure.WorkSessionClosureId &&
                value.Treatment == "SurplusIncome" &&
                value.AccountingStatus == AccountingPostingStatuses.Posted);
        }
        using (var retry = await accounting.PostAsync($"/api/commerce/v1/accounting/postings/{invoice.DocumentId:D}/retry", null))
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var posting = await retry.Content.ReadFromJsonAsync<AccountingPostingView>();
            Assert.NotNull(posting); Assert.Equal(AccountingPostingStatuses.Posted, posting.Status); Assert.NotNull(posting.EntryId);
        }
        await AssertBalancedAsync(invoice.DocumentId);
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", invoice.DocumentId));

        using (var replay = fixture.CreateUploadMessage(invoice))
        using (var replayResponse = await fixture.CreateClient().SendAsync(replay))
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", invoice.DocumentId));

        using (var entryResponse = await accounting.GetAsync($"/api/commerce/v1/accounting/entries/by-document/{invoice.DocumentId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, entryResponse.StatusCode);
            var entry = await entryResponse.Content.ReadFromJsonAsync<AccountingEntryView>();
            Assert.NotNull(entry); Assert.StartsWith("ASI-", entry.EntryNumber); Assert.Equal(entry.DebitTotal, entry.CreditTotal); Assert.True(entry.Lines.Count >= 3);
        }

        var customerId = await CreateCustomerAsync();
        var creditBase = fixture.CreateValidRequest(9_813) with
        {
            Payments = [],
            CustomerId = customerId,
            Credit = new PosSaleCreditContract(
                customerId, 11_900m,
                new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.FromHours(-5)))
        };
        var creditInvoice = WithUblSnapshot(creditBase);
        creditInvoice = creditInvoice with
        {
            UblSnapshot = creditInvoice.UblSnapshot! with
            {
                PaymentFormCode = "2",
                DueDate = new DateOnly(2026, 8, 31)
            }
        };
        await SetWarehouseNegativeSalesPolicyAsync(true);
        try
        {
            using var upload = fixture.CreateUploadMessage(creditInvoice);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await SetWarehouseNegativeSalesPolicyAsync(true);
        }
        await AssertBalancedAsync(creditInvoice.DocumentId);
        var receivableId = await ScalarAsync<Guid>(
            "SELECT ReceivableId FROM dbo.Receivables WHERE SourceDocumentId=@Id",
            creditInvoice.DocumentId);
        var creditAccounts = await accounting.GetFromJsonAsync<AccountingAccountView[]>(
            "/api/commerce/v1/accounting/accounts") ?? [];
        var otherIncomeAccountId = Assert.Single(
            creditAccounts, item => item.Code == "429595").AccountId;
        var receivableDebitId = Guid.NewGuid();
        var receivableAdjustment = new ConfirmAccountAdjustmentRequest(
            receivableDebitId, fixture.BusinessId, AccountingSubledgerKinds.Receivable,
            receivableId, AccountingAdjustmentDirections.Increase, 500m,
            otherIncomeAccountId, null,
            new DateTimeOffset(2026, 8, 1, 8, 30, 0, TimeSpan.FromHours(-5)),
            "ND-CLIENTE", "Mayor valor a cargo del cliente");
        using (var response = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/manual/account-adjustments",
                   receivableAdjustment))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await AssertBalancedAsync(receivableDebitId);
        Assert.Equal(12_400m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivableId));
        Assert.Equal(500m, await AccountAmountAsync(
            receivableDebitId, "130505", debit: true));
        var receivableCreditId = Guid.NewGuid();
        using (var response = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/manual/account-adjustments",
                   receivableAdjustment with
                   {
                       AdjustmentId = receivableCreditId,
                       Direction = AccountingAdjustmentDirections.Decrease,
                       Description = "Menor valor a cargo del cliente"
                   }))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await AssertBalancedAsync(receivableCreditId);
        Assert.Equal(11_900m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivableId));
        Assert.Equal(-500m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ReceivableTransactions WHERE SourceDocumentId=@Id",
            receivableCreditId));

        var nonStockProductId = await CreateNonStockProductAsync();
        await ConfigureTargetMarginsAsync(fixture.ProductId, nonStockProductId);
        var receivedAt = new DateTimeOffset(
            2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"COMP-{Guid.NewGuid():N}",
            receivedAt.AddDays(-1), receivedAt, true, receivedAt.AddDays(30),
            "COP", "Compra contable mixta",
            [
                new GoodsReceiptLineRequest(
                    1, fixture.ProductId, "Inventario", 10m, 5_000m, 0m,
                    "01", 19m, PurchasingTaxTreatments.DeductibleInputVat),
                new GoodsReceiptLineRequest(
                    2, nonStockProductId, "Servicio no inventariable", 2m,
                    10_000m, 0m, "01", 19m,
                    PurchasingTaxTreatments.CapitalizedCost)
            ]);
        var receiptKey = $"accounting-receipt-{receipt.DocumentId:N}";
        using (var message = CreateGoodsReceiptMessage(receipt, receiptKey))
        using (var response = await accounting.SendAsync(message))
            Assert.True(
                response.StatusCode == HttpStatusCode.Accepted,
                await response.Content.ReadAsStringAsync());

        Assert.Equal(
            AccountingPostingStatuses.Posted,
            await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                receipt.DocumentId));
        await AssertBalancedAsync(receipt.DocumentId);
        await AssertFastProcessingAsync(receipt.DocumentId, "entrada de mercancía");
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", receipt.DocumentId));

        var payableId = await ScalarAsync<Guid>(
            "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id",
            receipt.DocumentId);
        var accounts = await accounting.GetFromJsonAsync<AccountingAccountView[]>(
            "/api/commerce/v1/accounting/accounts") ?? [];
        var expenseAccountId = Assert.Single(accounts, item => item.Code == "519595").AccountId;
        var bankAccountId = Assert.Single(accounts, item => item.Code == "111005").AccountId;
        var adjustmentId = Guid.NewGuid();
        var adjustment = new ConfirmAccountAdjustmentRequest(
            adjustmentId, fixture.BusinessId, AccountingSubledgerKinds.Payable,
            payableId, AccountingAdjustmentDirections.Increase, 2_000m,
            expenseAccountId, null, receivedAt.AddMinutes(1), "ND-PROVEEDOR",
            "Mayor valor reconocido al proveedor");
        using (var response = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/manual/account-adjustments", adjustment))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await AssertBalancedAsync(adjustmentId);
        Assert.Equal(0, await CountAsync(
            "DocumentProcessingJobs", "DocumentId", adjustmentId));
        Assert.Equal(1, await CountAsync(
            "AccountingSourceDocuments", "SourceDocumentId", adjustmentId));
        Assert.Equal(1, await CountAsync(
            "AccountingPostingJobs", "SourceDocumentId", adjustmentId));
        Assert.Equal(85_300m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(2_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id",
            adjustmentId));
        Assert.Equal(2_000m, await AccountAmountAsync(adjustmentId, "519595", debit: true));
        Assert.Equal(2_000m, await AccountAmountAsync(adjustmentId, "220505", debit: false));
        using (var duplicate = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/manual/account-adjustments", adjustment))
        {
            duplicate.EnsureSuccessStatusCode();
            var accepted = await duplicate.Content
                .ReadFromJsonAsync<AccountingManualDocumentAcceptance>();
            Assert.True(accepted!.IsDuplicate);
        }
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", adjustmentId));

        var creditAdjustmentId = Guid.NewGuid();
        using (var response = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/manual/account-adjustments",
                   adjustment with
                   {
                       AdjustmentId = creditAdjustmentId,
                       Direction = AccountingAdjustmentDirections.Decrease,
                       Description = "Menor valor reconocido por nota crédito"
                   }))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await AssertBalancedAsync(creditAdjustmentId);
        Assert.Equal(83_300m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(-2_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id",
            creditAdjustmentId));
        Assert.Equal(2_000m, await AccountAmountAsync(
            creditAdjustmentId, "220505", debit: true));
        Assert.Equal(2_000m, await AccountAmountAsync(
            creditAdjustmentId, "519595", debit: false));

        var voucherId = Guid.NewGuid();
        var voucher = new ConfirmManualAccountingVoucherRequest(
            voucherId, fixture.BusinessId, receivedAt.AddMinutes(2), "AJUSTE",
            "Comprobante manual balanceado",
            [
                new ManualVoucherLineRequest(expenseAccountId, null, null,
                    "Debito del ajuste", 750m, 0m),
                new ManualVoucherLineRequest(bankAccountId, null, null,
                    "Credito del ajuste", 0m, 750m)
            ]);
        using (var response = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/manual/vouchers", voucher))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await AssertBalancedAsync(voucherId);
        Assert.Equal(0, await CountAsync(
            "DocumentProcessingJobs", "DocumentId", voucherId));
        Assert.Equal(1, await CountAsync(
            "AccountingSourceDocuments", "SourceDocumentId", voucherId));
        Assert.Equal(750m, await AccountAmountAsync(voucherId, "519595", debit: true));
        Assert.Equal(750m, await AccountAmountAsync(voucherId, "111005", debit: false));

        using (var invalid = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/manual/vouchers",
                   voucher with
                   {
                       VoucherId = Guid.NewGuid(),
                       Lines =
                       [
                           new ManualVoucherLineRequest(expenseAccountId, null, null,
                               "Debito", 100m, 0m),
                           new ManualVoucherLineRequest(bankAccountId, null, null,
                               "Credito no balanceado", 0m, 99m)
                       ]
                   }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(50_000m, await AccountAmountAsync(
            receipt.DocumentId, "143505", debit: true));
        Assert.Equal(9_500m, await AccountAmountAsync(
            receipt.DocumentId, "240810", debit: true));
        Assert.Equal(23_800m, await AccountAmountAsync(
            receipt.DocumentId, "519595", debit: true));
        Assert.Equal(83_300m, await AccountAmountAsync(
            receipt.DocumentId, "220505", debit: false));
        Assert.Equal(83_300m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id",
            receipt.DocumentId));
        Assert.True(await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.Suppliers s ON s.PartyId=l.PartyId
            WHERE e.SourceDocumentId=@Id AND s.SupplierId=
              (SELECT SupplierId FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id);
            """, receipt.DocumentId) > 0);

        using (var duplicate = CreateGoodsReceiptMessage(receipt, receiptKey))
        using (var duplicateResponse = await accounting.SendAsync(duplicate))
            Assert.Equal(HttpStatusCode.Accepted, duplicateResponse.StatusCode);
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", receipt.DocumentId));


        var withholdingRules = new[]
        {
            new SaveWithholdingRuleRequest(
                fixture.BusinessId, "RF-COMPRAS", "Retefuente compras", WithholdingKinds.IncomeTax,
                WithholdingDirections.Purchase, WithholdingRecognitionMoments.Accrual, WithholdingBaseKinds.TaxExclusiveAmount,
                "MERCANCIA", null, 2.5m, 0m, ["O-23"], new DateOnly(2026, 1, 1), null, true),
            new SaveWithholdingRuleRequest(
                fixture.BusinessId, "RIVA-COMPRAS", "ReteIVA compras", WithholdingKinds.Vat,
                WithholdingDirections.Purchase, WithholdingRecognitionMoments.Accrual, WithholdingBaseKinds.VatAmount,
                "MERCANCIA", null, 15m, 0m, ["O-23"], new DateOnly(2026, 1, 1), null, true),
            new SaveWithholdingRuleRequest(
                fixture.BusinessId, "RICA-BOG", "ReteICA Bogota", WithholdingKinds.IndustryCommerce,
                WithholdingDirections.Purchase, WithholdingRecognitionMoments.Accrual, WithholdingBaseKinds.TaxExclusiveAmount,
                "MERCANCIA", "11001", 1m, 0m, ["O-23"], new DateOnly(2026, 1, 1), null, true)
        };
        using (var profile = await accounting.PutAsJsonAsync(
            $"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId:D}",
            new SaveCounterpartyTaxProfileRequest(
                fixture.BusinessId, fixture.SupplierId, true, ["O-23"], "11001")))
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        foreach (var rule in withholdingRules)
        {
            using var response = await accounting.PostAsJsonAsync(
                "/api/commerce/v1/taxation/withholding-rules", rule);
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                await response.Content.ReadAsStringAsync());
        }

        using var expenseUser = fixture.CreateAdminClient(
            ExpensePermissionCodes.Read,
            ExpensePermissionCodes.Create,
            ExpensePermissionCodes.Configure);
        ExpenseWorkspaceOptions expenseOptions;
        using (var optionsResponse = await expenseUser.GetAsync(
                   "/api/commerce/v1/expenses/options"))
        {
            optionsResponse.EnsureSuccessStatusCode();
            expenseOptions = await optionsResponse.Content
                .ReadFromJsonAsync<ExpenseWorkspaceOptions>()
                ?? throw new InvalidOperationException("Expense options are empty.");
        }
        var expenseAccount = Assert.Single(
            expenseOptions.ExpenseAccounts, account => account.Code == "519595");
        var expenseCenter = Assert.Single(
            expenseOptions.CostCenters, center => center.IsDefault);
        var expenseConceptId = Guid.NewGuid();
        using (var conceptResponse = await expenseUser.PutAsJsonAsync(
                   $"/api/commerce/v1/expenses/concepts/{expenseConceptId:D}",
                   new SaveExpenseConceptRequest(
                       expenseConceptId, fixture.BusinessId, "SERVICIOS",
                       "Servicios operativos", expenseAccount.AccountId,
                       expenseCenter.CostCenterId, "MERCANCIA", true)))
            Assert.Equal(HttpStatusCode.OK, conceptResponse.StatusCode);

        var expenseId = Guid.NewGuid();
        using (var expenseMessage = new HttpRequestMessage(
                   HttpMethod.Post, "/api/commerce/v1/expenses/confirm")
               {
                   Content = JsonContent.Create(new ConfirmExpenseRequest(
                       expenseId, fixture.BusinessId, fixture.SupplierId,
                       expenseConceptId, null, $"GASTO-{Guid.NewGuid():N}",
                       receivedAt.AddHours(3), receivedAt.AddDays(30), "COP",
                       "Servicio con retenciones y centro de costo", 100_000m,
                       19_000m, "11001", null))
               })
        {
            expenseMessage.Headers.Add("Idempotency-Key", $"expense-{expenseId:N}");
            using var expenseResponse = await expenseUser.SendAsync(expenseMessage);
            Assert.Equal(HttpStatusCode.Accepted, expenseResponse.StatusCode);
        }
        Assert.Equal(AccountingPostingStatuses.Posted,
            await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                expenseId));
        await AssertBalancedAsync(expenseId);
        await AssertFastProcessingAsync(expenseId, "gasto");
        Assert.Equal(119_000m, await ScalarAsync<decimal>(
            "SELECT GrossAmount FROM dbo.Expenses WHERE ExpenseId=@Id", expenseId));
        Assert.Equal(6_350m, await ScalarAsync<decimal>(
            "SELECT WithholdingAmount FROM dbo.Expenses WHERE ExpenseId=@Id", expenseId));
        Assert.Equal(112_650m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id", expenseId));
        Assert.Equal(100_000m, await AccountAmountAsync(expenseId, "519595", debit: true));
        Assert.Equal(19_000m, await AccountAmountAsync(expenseId, "240810", debit: true));
        Assert.Equal(112_650m, await AccountAmountAsync(expenseId, "220505", debit: false));
        Assert.Equal(2_500m, await AccountAmountAsync(expenseId, "236540", debit: false));
        Assert.Equal(2_850m, await AccountAmountAsync(expenseId, "236701", debit: false));
        Assert.Equal(1_000m, await AccountAmountAsync(expenseId, "236805", debit: false));
        using (var expenseList = await expenseUser.GetAsync(
                   "/api/commerce/v1/expenses?page=1&pageSize=25"))
        {
            expenseList.EnsureSuccessStatusCode();
            var page = await expenseList.Content.ReadFromJsonAsync<ExpensePage>();
            Assert.Contains(page!.Items, item => item.ExpenseId == expenseId &&
                item.NetPayable == 112_650m);
        }

        var withheldReceipt = new ConfirmGoodsReceiptRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"RET-{Guid.NewGuid():N}", receivedAt,
            receivedAt.AddHours(1), true, receivedAt.AddDays(30), "COP",
            "Compra con retenciones inmutables",
            [new GoodsReceiptLineRequest(
                1, fixture.ProductId, "Mercancia con retenciones", 10m,
                10_000m, 0m, "01", 19m,
                PurchasingTaxTreatments.DeductibleInputVat)],
            WithholdingConceptCode: "MERCANCIA");
        using (var message = CreateGoodsReceiptMessage(
            withheldReceipt, $"withholding-{withheldReceipt.DocumentId:N}"))
        using (var response = await accounting.SendAsync(message))
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                await response.Content.ReadAsStringAsync());

        Assert.Equal(AccountingPostingStatuses.Posted,
            await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                withheldReceipt.DocumentId));
        await AssertBalancedAsync(withheldReceipt.DocumentId);
        await AssertFastProcessingAsync(withheldReceipt.DocumentId, "entrada con retenciones");
        Assert.Equal(119_000m, await ScalarAsync<decimal>(
            "SELECT GrossAmount FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id",
            withheldReceipt.DocumentId));
        Assert.Equal(6_350m, await ScalarAsync<decimal>(
            "SELECT WithholdingTotal FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id",
            withheldReceipt.DocumentId));
        Assert.Equal(112_650m, await ScalarAsync<decimal>(
            "SELECT NetAmount FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id",
            withheldReceipt.DocumentId));
        Assert.Equal(112_650m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id",
            withheldReceipt.DocumentId));
        Assert.Equal(100_000m, await AccountAmountAsync(
            withheldReceipt.DocumentId, "143505", debit: true));
        Assert.Equal(19_000m, await AccountAmountAsync(
            withheldReceipt.DocumentId, "240810", debit: true));
        Assert.Equal(112_650m, await AccountAmountAsync(
            withheldReceipt.DocumentId, "220505", debit: false));
        Assert.Equal(2_500m, await AccountAmountAsync(
            withheldReceipt.DocumentId, "236540", debit: false));
        Assert.Equal(2_850m, await AccountAmountAsync(
            withheldReceipt.DocumentId, "236701", debit: false));
        Assert.Equal(1_000m, await AccountAmountAsync(
            withheldReceipt.DocumentId, "236805", debit: false));
        var purchaseReturn = new ConfirmPurchaseReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, receipt.DocumentId,
            new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(-5)),
            "QualityIssue", "Devolucion contable de compra",
            [new PurchaseReturnLineRequest(1, 2m), new PurchaseReturnLineRequest(2, 1m)]);
        using (var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/purchase-returns/confirm")
            { Content = JsonContent.Create(purchaseReturn) })
        {
            message.Headers.Add("Idempotency-Key", $"accounting-purchase-return-{purchaseReturn.ReturnId:N}");
            using var response = await accounting.SendAsync(message);
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(AccountingPostingStatuses.Posted,
            await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                purchaseReturn.ReturnId));
        await AssertBalancedAsync(purchaseReturn.ReturnId);
        await AssertFastProcessingAsync(purchaseReturn.ReturnId, "devolución a proveedor");
        Assert.Equal(23_800m, await AccountAmountAsync(
            purchaseReturn.ReturnId, "220505", debit: true));
        Assert.Equal(10_000m, await AccountAmountAsync(
            purchaseReturn.ReturnId, "143505", debit: false));
        Assert.Equal(1_900m, await AccountAmountAsync(
            purchaseReturn.ReturnId, "240810", debit: false));
        Assert.Equal(11_900m, await AccountAmountAsync(
            purchaseReturn.ReturnId, "519595", debit: false));
        var receiptWithoutSettlement = receipt with
        {
            DocumentId = Guid.NewGuid(),
            SupplierInvoiceNumber = $"SIN-PAGO-{Guid.NewGuid():N}",
            CreatesPayable = false,
            DueDate = null,
            Lines =
            [
                new GoodsReceiptLineRequest(
                    1, fixture.ProductId, "Sin evidencia de pago", 1m, 1_000m,
                    0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)
            ]
        };
        using (var message = CreateGoodsReceiptMessage(
            receiptWithoutSettlement,
            $"accounting-unsettled-{receiptWithoutSettlement.DocumentId:N}"))
        using (var response = await accounting.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            AccountingPostingStatuses.PendingConfiguration,
            await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                receiptWithoutSettlement.DocumentId));
        Assert.Equal(
            "SettlementSourceMissing",
            await ScalarAsync<string>(
                "SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                receiptWithoutSettlement.DocumentId));
        Assert.Equal(0, await CountAsync(
            "AccountingEntries", "SourceDocumentId",
            receiptWithoutSettlement.DocumentId));

        var returnRequest = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, invoice.DocumentId,
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, "Cash", "Devolucion contable",
            [new ConfirmSalesReturnLineRequest(1, .5m, ReturnInventoryDispositions.Sellable)],
            fixture.WorkSessionId, 1, "Other");
        using (var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
        { Content = JsonContent.Create(returnRequest) })
        {
            message.Headers.Add("Idempotency-Key", $"accounting-return-{returnRequest.ReturnId:N}");
            using var response = await accounting.SendAsync(message);
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(AccountingPostingStatuses.Posted,
            await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", returnRequest.ReturnId));
        await AssertBalancedAsync(returnRequest.ReturnId);
        await AssertFastProcessingAsync(returnRequest.ReturnId, "devolución de venta");
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", returnRequest.ReturnId));

        await AssertTenSalePipelineIsExactAndIdempotentAsync();
        await AssertAccumulatedCodesForTenSalesReturnsAndCreditNotesAsync(
            accounting, customerId);

        using (var report = await accounting.GetAsync("/api/commerce/v1/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31"))
        {
            Assert.Equal(HttpStatusCode.OK, report.StatusCode);
            var rows = await report.Content.ReadFromJsonAsync<IReadOnlyList<TrialBalanceRow>>();
            Assert.NotNull(rows); Assert.NotEmpty(rows);
            Assert.Equal(rows.Sum(row => row.Debit), rows.Sum(row => row.Credit));
        }
        using (var response = await accounting.GetAsync(
                   "/api/commerce/v1/accounting/reports/journal?from=2026-01-01&to=2026-12-31"))
        {
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<AccountingJournalRow[]>() ?? [];
            Assert.NotEmpty(rows);
            Assert.Equal(rows.Sum(row => row.Debit), rows.Sum(row => row.Credit));
            Assert.Contains(rows, row => row.SourceDocumentId == voucherId);
        }
        using (var response = await accounting.GetAsync(
                   "/api/commerce/v1/accounting/reports/general-ledger?from=2026-01-01&to=2026-12-31"))
        {
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<GeneralLedgerRow[]>() ?? [];
            Assert.NotEmpty(rows);
            Assert.Contains(rows, row => row.AccountCode == "220505");
        }
        using (var response = await accounting.GetAsync(
                   "/api/commerce/v1/accounting/reports/balance-sheet?asOf=2026-12-31"))
        {
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<FinancialStatementRow[]>() ?? [];
            Assert.Contains(rows, row => row.Section == "Asset");
            Assert.Contains(rows, row => row.Section == "Liability");
            Assert.Equal(rows.Where(row => row.Section == "Asset").Sum(row => row.Amount),
                rows.Where(row => row.Section is "Liability" or "Equity")
                    .Sum(row => row.Amount));
        }
        using (var response = await accounting.GetAsync(
                   "/api/commerce/v1/accounting/reports/income-statement?from=2026-01-01&to=2026-12-31"))
        {
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<FinancialStatementRow[]>() ?? [];
            Assert.Contains(rows, row => row.Section == "Revenue");
            Assert.Contains(rows, row => row.Section == "Expense");
        }
        using (var response = await accounting.GetAsync(
                   "/api/commerce/v1/accounting/reports/exceptions?from=2026-01-01&to=2026-12-31"))
        {
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<AccountingExceptionRow[]>() ?? [];
            Assert.Contains(rows, row => row.SourceDocumentId == receiptWithoutSettlement.DocumentId &&
                row.ErrorCode == "SettlementSourceMissing");
        }

        using (var definitionsResponse = await accounting.GetAsync(
                   "/api/commerce/v1/accounting/compliance/definitions?taxYear=2026"))
        {
            definitionsResponse.EnsureSuccessStatusCode();
            var reportDefinitions = await definitionsResponse.Content
                .ReadFromJsonAsync<ComplianceReportDefinitionView[]>() ?? [];
            Assert.Contains(reportDefinitions, item => item.FormatCode == "1001" &&
                item.FormatVersion == 11 && item.ResolutionNumber.Contains("000237/2025"));
            Assert.Contains(reportDefinitions, item => item.FormatCode == "1005" &&
                item.FormatVersion == 9 && item.SourceSha256.Length == 64);
            Assert.Contains(reportDefinitions, item => item.FormatCode == "1008" &&
                item.FormatVersion == 7);
        }

        using (var blockedResponse = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/compliance/runs",
                   new GenerateComplianceReportRequest(
                       "DIAN", 2026, "FORM-310", 1,
                       new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))))
        {
            Assert.Equal(HttpStatusCode.Created, blockedResponse.StatusCode);
            var blocked = await blockedResponse.Content
                .ReadFromJsonAsync<ComplianceReportRunView>();
            Assert.NotNull(blocked);
            Assert.Equal("Blocked", blocked.Status);
            Assert.Contains(blocked.Validations,
                item => item.Code == "CONCEPT_MAPPING_REQUIRED");
        }

        var inputVatAccount = Assert.Single(accounts, item => item.Code == "240810");
        using (var mappingResponse = await accounting.PutAsJsonAsync(
                   "/api/commerce/v1/accounting/compliance/mappings",
                   new SetComplianceConceptMappingRequest(
                       fixture.BusinessId, "DIAN", 2026, "IVA", 1,
                       inputVatAccount.AccountId, "IVA-DESCONTABLE", "inputVat")))
        {
            mappingResponse.EnsureSuccessStatusCode();
            var complianceMapping = await mappingResponse.Content
                .ReadFromJsonAsync<ComplianceConceptMappingView>();
            Assert.NotNull(complianceMapping);
            Assert.Equal(fixture.BusinessId, complianceMapping.BusinessId);
            Assert.Equal("inputVat", complianceMapping.TargetField);
        }

        ComplianceReportRunView readyRun;
        using (var readyResponse = await accounting.PostAsJsonAsync(
                   "/api/commerce/v1/accounting/compliance/runs",
                   new GenerateComplianceReportRequest(
                       "DIAN", 2026, "IVA", 1,
                       new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))))
        {
            Assert.Equal(HttpStatusCode.Created, readyResponse.StatusCode);
            readyRun = await readyResponse.Content
                .ReadFromJsonAsync<ComplianceReportRunView>()
                ?? throw new InvalidOperationException("The compliance run is empty.");
            Assert.Equal("Ready", readyRun.Status);
            Assert.True(readyRun.RowCount > 0);
            Assert.NotEqual(0m, readyRun.ControlTotal);
            Assert.Empty(readyRun.Validations);
        }
        using (var artifactResponse = await accounting.GetAsync(
                   $"/api/commerce/v1/accounting/compliance/runs/{readyRun.RunId:D}/artifact"))
        {
            artifactResponse.EnsureSuccessStatusCode();
            var content = await artifactResponse.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(content);
            Assert.Contains("Authority=DIAN;TaxYear=2026;Format=IVA;Version=1",
                System.Text.Encoding.UTF8.GetString(content));
            var persistedHash = await ScalarAsync<byte[]>(
                "SELECT ContentSha256 FROM compliance.ComplianceReportArtifacts WHERE RunId=@Id",
                readyRun.RunId);
            Assert.Equal(System.Security.Cryptography.SHA256.HashData(content), persistedHash);
        }

        var nextPeriod = Guid.NewGuid();
        using (var create = await accounting.PostAsJsonAsync("/api/commerce/v1/accounting/periods",
            new CreateAccountingPeriodRequest(nextPeriod, fixture.TenantId, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), "2027")))
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using (var close = await accounting.PostAsync($"/api/commerce/v1/accounting/periods/{nextPeriod:D}/close", null))
            Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.Equal("Closed", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPeriods WHERE PeriodId=@Id", nextPeriod));
    }

    private async Task AssertTenSalePipelineIsExactAndIdempotentAsync()
    {
        var sales = Enumerable.Range(0, 10)
            .Select(index => WithUblSnapshot(fixture.CreateValidRequest(9_820 + index)))
            .ToArray();
        var documentIds = sales.Select(sale => sale.DocumentId).ToArray();
        var quantityBefore = await InventoryQuantityAsync();
        var dailyBefore = await ProductDailyTotalAsync();

        await SetWarehouseNegativeSalesPolicyAsync(true);
        foreach (var sale in sales)
        {
            using var upload = fixture.CreateUploadMessage(sale);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
        }

        var firstPass = await ReadPipelineSnapshotAsync(documentIds);
        Assert.Equal(10, firstPass.OperationalJobs);
        Assert.Equal(10, firstPass.InventoryMovements);
        Assert.Equal(-10m, firstPass.InventoryQuantityChange);
        Assert.Equal(quantityBefore - 10m, firstPass.InventoryQuantityAfter);
        Assert.Equal(10, firstPass.AccountingJobs);
        Assert.Equal(10, firstPass.AccountingEntries);
        Assert.Equal(0, firstPass.UnbalancedEntries);
        Assert.Equal(119_000m, firstPass.CashDebit);
        Assert.Equal(100_000m, firstPass.SalesRevenueCredit);
        Assert.Equal(19_000m, firstPass.OutputVatCredit);
        Assert.Equal(10, firstPass.ReportDocuments);
        Assert.Equal(119_000m, firstPass.ReportDocumentTotal);
        Assert.Equal(10, firstPass.ReportLineFacts);
        Assert.Equal(10m, firstPass.ReportQuantity);
        Assert.Equal(119_000m, firstPass.ReportLineTotal);
        Assert.InRange(firstPass.MaxOperationalMicroseconds, 1, 2_000_000);
        Assert.InRange(firstPass.MaxAccountingMicroseconds, 1, 2_000_000);
        Assert.InRange(firstPass.MaxReportingMicroseconds, 0, 2_000_000);
        Console.WriteLine(
            $"PIPELINE_10_MAX_MS operation={firstPass.MaxOperationalMicroseconds / 1000m:F3} " +
            $"accounting={firstPass.MaxAccountingMicroseconds / 1000m:F3} " +
            $"reporting_after_operation={firstPass.MaxReportingMicroseconds / 1000m:F3}");

        await AssertInventoryMovementChainAsync(documentIds, quantityBefore);
        var dailyAfter = await ProductDailyTotalAsync();
        Assert.Equal(dailyBefore.DocumentCount + 10, dailyAfter.DocumentCount);
        Assert.Equal(dailyBefore.Quantity + 10m, dailyAfter.Quantity);
        Assert.Equal(dailyBefore.NetTotalSales + 119_000m, dailyAfter.NetTotalSales);

        foreach (var sale in sales)
        {
            using var replay = fixture.CreateUploadMessage(sale);
            using var response = await fixture.CreateClient().SendAsync(replay);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var replayPass = await ReadPipelineSnapshotAsync(documentIds);
        Assert.Equal(firstPass, replayPass);
        Assert.Equal(dailyAfter, await ProductDailyTotalAsync());
    }

    private async Task AssertAccumulatedCodesForTenSalesReturnsAndCreditNotesAsync(
        HttpClient accounting, Guid customerId)
    {
        const decimal creditNoteAmount = 7_100m;

        var accounts = await accounting.GetFromJsonAsync<AccountingAccountView[]>(
            "/api/commerce/v1/accounting/accounts") ?? [];
        var salesReturnsAccountId = Assert.Single(
            accounts, account => account.Code == "417595").AccountId;
        var sales = new List<PosSaleUploadRequest>(10);
        var returns = new List<ConfirmSalesReturnRequest>(10);
        var notes = new List<ConfirmAccountAdjustmentRequest>(10);
        var receivableIds = new List<Guid>(10);

        await SetWarehouseNegativeSalesPolicyAsync(true);
        for (var index = 0; index < 10; index++)
        {
            var sale = CreateAccumulationCreditSale(9_840 + index, customerId);
            sales.Add(sale);
            using (var upload = fixture.CreateUploadMessage(sale))
            using (var response = await fixture.CreateClient().SendAsync(upload))
                Assert.True(response.StatusCode == HttpStatusCode.OK,
                    await response.Content.ReadAsStringAsync());

            var receivableId = await ScalarAsync<Guid>(
                "SELECT ReceivableId FROM dbo.Receivables WHERE SourceDocumentId=@Id",
                sale.DocumentId);
            receivableIds.Add(receivableId);

            var salesReturn = new ConfirmSalesReturnRequest(
                Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                sale.DocumentId,
                new DateTimeOffset(2026, 8, 10, 10, index, 0,
                    TimeSpan.FromHours(-5)),
                ReturnEconomicResolutions.CustomerCredit, null,
                $"Devolución acumulativa {index + 1}",
                [new ConfirmSalesReturnLineRequest(
                    1, 1m, ReturnInventoryDispositions.Sellable)],
                null, null, "Other");
            returns.Add(salesReturn);
            using (var message = CreateSalesReturnMessage(
                       salesReturn, $"acc-return-{salesReturn.ReturnId:N}"))
            using (var response = await accounting.SendAsync(message))
                Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                    await response.Content.ReadAsStringAsync());

            var creditNote = new ConfirmAccountAdjustmentRequest(
                Guid.NewGuid(), fixture.BusinessId,
                AccountingSubledgerKinds.Receivable, receivableId,
                AccountingAdjustmentDirections.Decrease, creditNoteAmount,
                salesReturnsAccountId, null,
                new DateTimeOffset(2026, 8, 10, 11, index, 0,
                    TimeSpan.FromHours(-5)),
                $"NC-ACUM-{index + 1:D2}",
                $"Nota crédito acumulativa {index + 1}");
            notes.Add(creditNote);
            using var noteResponse = await accounting.PostAsJsonAsync(
                "/api/commerce/v1/accounting/manual/account-adjustments",
                creditNote);
            Assert.Equal(HttpStatusCode.Accepted, noteResponse.StatusCode);
        }

        var allDocumentIds = sales.Select(item => item.DocumentId)
            .Concat(returns.Select(item => item.ReturnId))
            .Concat(notes.Select(item => item.AdjustmentId))
            .ToArray();
        var firstPass = await ReadAccumulatedAccountingSnapshotAsync(
            allDocumentIds, receivableIds);

        Assert.Equal(30, firstPass.AccountingSources);
        Assert.Equal(30, firstPass.AccountingJobs);
        Assert.Equal(30, firstPass.AccountingEntries);
        Assert.Equal(0, firstPass.UnbalancedEntries);
        Assert.Equal(20, firstPass.OperationalJobs);
        Assert.Equal(20, firstPass.InventoryMovements);
        Assert.Equal(20, firstPass.ReportingJobs);
        Assert.Equal(20, firstPass.ReportLineFacts);
        Assert.Equal(1_000_000m, firstPass.ReceivablesOutstanding);
        Assert.Equal(1_190_000m, firstPass.AccountsReceivableDebit);
        Assert.Equal(190_000m, firstPass.AccountsReceivableCredit);
        Assert.Equal(1_000_000m,
            firstPass.AccountsReceivableDebit - firstPass.AccountsReceivableCredit);
        Assert.Equal(1_000_000m, firstPass.SalesRevenueCredit);
        Assert.Equal(190_000m, firstPass.OutputVatCredit);
        Assert.Equal(19_000m, firstPass.OutputVatDebit);
        Assert.Equal(171_000m, firstPass.SalesReturnsDebit);
        Assert.Equal(firstPass.CostOfGoodsSoldDebit, firstPass.InventoryCredit);
        Assert.Equal(firstPass.CostOfGoodsSoldCredit, firstPass.InventoryDebit);
        Assert.Equal(
            1_380_000m + firstPass.CostOfGoodsSoldDebit + firstPass.InventoryDebit,
            firstPass.TotalDebits);
        Assert.Equal(firstPass.TotalDebits, firstPass.TotalCredits);
        Assert.Equal(1_190_000m, firstPass.ReportSalesTotal);
        Assert.Equal(119_000m, firstPass.ReportReturnedTotal);
        Assert.Equal(1_071_000m, firstPass.ReportNetLineTotal);

        foreach (var sale in sales)
        {
            using var replay = fixture.CreateUploadMessage(sale);
            using var response = await fixture.CreateClient().SendAsync(replay);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        foreach (var salesReturn in returns)
        {
            using var replay = CreateSalesReturnMessage(
                salesReturn, $"acc-return-{salesReturn.ReturnId:N}");
            using var response = await accounting.SendAsync(replay);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        foreach (var note in notes)
        {
            using var response = await accounting.PostAsJsonAsync(
                "/api/commerce/v1/accounting/manual/account-adjustments", note);
            response.EnsureSuccessStatusCode();
            var acceptance = await response.Content
                .ReadFromJsonAsync<AccountingManualDocumentAcceptance>();
            Assert.True(acceptance!.IsDuplicate);
        }

        Assert.Equal(firstPass, await ReadAccumulatedAccountingSnapshotAsync(
            allDocumentIds, receivableIds));
        Console.WriteLine(
            "ACCOUNTING_ACCUMULATION_10 sales_base=1000000 " +
            "returns_base=100000 credit_notes=71000 final_receivable=1000000");
    }

    private PosSaleUploadRequest CreateAccumulationCreditSale(
        long consecutive, Guid customerId)
    {
        const decimal quantity = 10m;
        const decimal unitPrice = 10_000m;
        const decimal untaxed = 100_000m;
        const decimal tax = 19_000m;
        const decimal total = 119_000m;
        var source = fixture.CreateValidRequest(consecutive);
        var fiscal = source.FiscalSnapshot!;
        var cufe = CufeCalculator.Calculate(
            new CufeInput(
                fiscal.FiscalNumber, fiscal.IssuedAt, untaxed, total,
                ServerSliceFixture.SupplierTaxId, "222222222",
                new FiscalTechnicalKey(
                    ServerSliceFixture.TechnicalKeyValue,
                    ServerSliceFixture.TechnicalKeyVersion),
                FiscalEnvironment.Test,
                [new FiscalTaxAmount("01", tax)]),
            ServerSliceFixture.QrValidationUrl);
        var sale = source with
        {
            CustomerId = customerId,
            Payments = [],
            Credit = new PosSaleCreditContract(
                customerId, total,
                new DateTimeOffset(2026, 8, 31, 0, 0, 0,
                    TimeSpan.FromHours(-5))),
            CommercialSnapshot = source.CommercialSnapshot with
            {
                Taxes = [new PosSaleTaxContract("01", tax)],
                UntaxedAmount = untaxed,
                TaxAmount = tax,
                PayableAmount = total
            },
            FiscalSnapshot = fiscal with
            {
                Taxes = [new PosSaleTaxContract("01", tax)],
                UntaxedAmount = untaxed,
                TaxAmount = tax,
                PayableAmount = total,
                Cufe = cufe.Cufe,
                QrPayload = cufe.QrPayload
            },
            Lines =
            [
                new PosSaleLineContract(
                    1, fixture.ProductId, "Producto acumulación", "01",
                    quantity, unitPrice, 0m, tax, untaxed, total, 19m)
            ]
        };
        sale = WithUblSnapshot(sale);
        return sale with
        {
            UblSnapshot = sale.UblSnapshot! with
            {
                PaymentFormCode = "2",
                DueDate = new DateOnly(2026, 8, 31)
            }
        };
    }

    private async Task<AccumulatedAccountingSnapshot>
        ReadAccumulatedAccountingSnapshotAsync(
            IReadOnlyList<Guid> documentIds, IReadOnlyList<Guid> receivableIds)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var documentParameters = documentIds.Select((id, index) =>
        {
            var name = $"@DocumentId{index}";
            command.Parameters.AddWithValue(name, id);
            return name;
        }).ToArray();
        var receivableParameters = receivableIds.Select((id, index) =>
        {
            var name = $"@ReceivableId{index}";
            command.Parameters.AddWithValue(name, id);
            return name;
        }).ToArray();
        var documents = string.Join(',', documentParameters);
        var receivables = string.Join(',', receivableParameters);
        command.CommandText = $"""
            SELECT
              (SELECT COUNT_BIG(*) FROM dbo.AccountingSourceDocuments
               WHERE SourceDocumentId IN ({documents})),
              (SELECT COUNT_BIG(*) FROM dbo.AccountingPostingJobs
               WHERE SourceDocumentId IN ({documents}) AND Status=N'Posted'),
              (SELECT COUNT_BIG(*) FROM dbo.AccountingEntries
               WHERE SourceDocumentId IN ({documents})),
              (SELECT COUNT_BIG(*) FROM dbo.AccountingEntries
               WHERE SourceDocumentId IN ({documents}) AND DebitTotal<>CreditTotal),
              (SELECT COUNT_BIG(*) FROM dbo.DocumentProcessingJobs
               WHERE DocumentId IN ({documents}) AND Status=N'Completed'),
              (SELECT COUNT_BIG(*) FROM dbo.InventoryMovements
               WHERE DocumentId IN ({documents})),
              (SELECT COUNT_BIG(*) FROM reporting.SalesReportingJobs
               WHERE SourceDocumentId IN ({documents}) AND Status=N'Projected'),
              (SELECT COUNT_BIG(*) FROM reporting.SalesReportLineFacts
               WHERE SourceDocumentId IN ({documents})),
              (SELECT COALESCE(SUM(OutstandingAmount),0) FROM dbo.Receivables
               WHERE ReceivableId IN ({receivables})),
              (SELECT COALESCE(SUM(l.Debit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'130505'),
              (SELECT COALESCE(SUM(l.Credit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'130505'),
              (SELECT COALESCE(SUM(l.Credit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'413595'),
              (SELECT COALESCE(SUM(l.Debit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'240805'),
              (SELECT COALESCE(SUM(l.Credit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'240805'),
              (SELECT COALESCE(SUM(l.Debit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'417595'),
              (SELECT COALESCE(SUM(l.Debit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'143505'),
              (SELECT COALESCE(SUM(l.Credit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'143505'),
              (SELECT COALESCE(SUM(l.Debit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'613595'),
              (SELECT COALESCE(SUM(l.Credit),0) FROM dbo.AccountingEntries e
               JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({documents}) AND a.Code=N'613595'),
              (SELECT COALESCE(SUM(DebitTotal),0) FROM dbo.AccountingEntries
               WHERE SourceDocumentId IN ({documents})),
              (SELECT COALESCE(SUM(CreditTotal),0) FROM dbo.AccountingEntries
               WHERE SourceDocumentId IN ({documents})),
              (SELECT COALESCE(SUM(TotalAmount),0) FROM reporting.SalesReportDocuments
               WHERE DocumentId IN ({documents})),
              (SELECT COALESCE(SUM(ReturnedTotalAmount),0) FROM reporting.SalesReportDocuments
               WHERE DocumentId IN ({documents})),
              (SELECT COALESCE(SUM(TotalAmount),0) FROM reporting.SalesReportLineFacts
               WHERE SourceDocumentId IN ({documents}));
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AccumulatedAccountingSnapshot(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
            reader.GetInt64(6), reader.GetInt64(7), reader.GetDecimal(8),
            reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11),
            reader.GetDecimal(12), reader.GetDecimal(13), reader.GetDecimal(14),
            reader.GetDecimal(15), reader.GetDecimal(16), reader.GetDecimal(17),
            reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20),
            reader.GetDecimal(21), reader.GetDecimal(22), reader.GetDecimal(23));
    }

    private static HttpRequestMessage CreateSalesReturnMessage(
        ConfirmSalesReturnRequest request, string idempotencyKey)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    private async Task<PipelineSnapshot> ReadPipelineSnapshotAsync(
        IReadOnlyList<Guid> documentIds)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var parameters = new string[documentIds.Count];
        for (var index = 0; index < documentIds.Count; index++)
        {
            parameters[index] = $"@Id{index}";
            command.Parameters.AddWithValue(parameters[index], documentIds[index]);
        }
        var ids = string.Join(',', parameters);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.CommandText = $"""
            SELECT
              (SELECT COUNT_BIG(*) FROM dbo.DocumentProcessingJobs
               WHERE DocumentId IN ({ids}) AND DocumentType=N'SalesInvoice' AND Status=N'Completed'),
              (SELECT COUNT_BIG(*) FROM dbo.InventoryMovements
               WHERE DocumentId IN ({ids}) AND DocumentType=N'SalesInvoice'),
              (SELECT COALESCE(SUM(QuantityChange),0) FROM dbo.InventoryMovements
               WHERE DocumentId IN ({ids}) AND DocumentType=N'SalesInvoice'),
              (SELECT QuantityOnHand FROM dbo.InventoryBalances
               WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId),
              (SELECT COUNT_BIG(*) FROM dbo.AccountingPostingJobs
               WHERE SourceDocumentId IN ({ids}) AND SourceDocumentType=N'SalesInvoice' AND Status=N'Posted'),
              (SELECT COUNT_BIG(*) FROM dbo.AccountingEntries
               WHERE SourceDocumentId IN ({ids}) AND SourceDocumentType=N'SalesInvoice'),
              (SELECT COUNT_BIG(*) FROM dbo.AccountingEntries
               WHERE SourceDocumentId IN ({ids}) AND SourceDocumentType=N'SalesInvoice'
                 AND DebitTotal<>CreditTotal),
              (SELECT COALESCE(SUM(l.Debit),0) FROM dbo.AccountingEntries e
               INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({ids}) AND a.Code=N'110505'),
              (SELECT COALESCE(SUM(l.Credit),0) FROM dbo.AccountingEntries e
               INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({ids}) AND a.Code=N'413595'),
              (SELECT COALESCE(SUM(l.Credit),0) FROM dbo.AccountingEntries e
               INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
               INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
               WHERE e.SourceDocumentId IN ({ids}) AND a.Code=N'240805'),
              (SELECT COUNT_BIG(*) FROM reporting.SalesReportDocuments
               WHERE DocumentId IN ({ids})),
              (SELECT COALESCE(SUM(TotalAmount),0) FROM reporting.SalesReportDocuments
               WHERE DocumentId IN ({ids})),
              (SELECT COUNT_BIG(*) FROM reporting.SalesReportLineFacts
               WHERE SourceDocumentId IN ({ids}) AND SourceDocumentType=N'SalesInvoice'),
              (SELECT COALESCE(SUM(Quantity),0) FROM reporting.SalesReportLineFacts
               WHERE SourceDocumentId IN ({ids}) AND SourceDocumentType=N'SalesInvoice'),
              (SELECT COALESCE(SUM(TotalAmount),0) FROM reporting.SalesReportLineFacts
               WHERE SourceDocumentId IN ({ids}) AND SourceDocumentType=N'SalesInvoice'),
              (SELECT MAX(DATEDIFF_BIG(microsecond,StartedAt,CompletedAt))
               FROM dbo.DocumentProcessingJobs WHERE DocumentId IN ({ids})),
              (SELECT MAX(DATEDIFF_BIG(microsecond,CreatedAt,CompletedAt))
               FROM dbo.AccountingPostingJobs WHERE SourceDocumentId IN ({ids})),
              (SELECT MAX(DATEDIFF_BIG(microsecond,StartedAt,CompletedAt))
               FROM reporting.SalesReportingJobs
               WHERE SourceDocumentId IN ({ids}) AND Status=N'Projected');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PipelineSnapshot(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetInt64(4), reader.GetInt64(5),
            reader.GetInt64(6), reader.GetDecimal(7), reader.GetDecimal(8),
            reader.GetDecimal(9), reader.GetInt64(10), reader.GetDecimal(11),
            reader.GetInt64(12), reader.GetDecimal(13), reader.GetDecimal(14),
            reader.GetInt64(15), reader.GetInt64(16), reader.GetInt64(17));
    }

    private async Task AssertInventoryMovementChainAsync(
        IReadOnlyList<Guid> documentIds, decimal quantityBefore)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var parameters = new string[documentIds.Count];
        for (var index = 0; index < documentIds.Count; index++)
        {
            parameters[index] = $"@Id{index}";
            command.Parameters.AddWithValue(parameters[index], documentIds[index]);
        }
        command.CommandText = $"""
            SELECT QuantityBefore,QuantityChange,QuantityAfter
            FROM dbo.InventoryMovements
            WHERE DocumentId IN ({string.Join(',', parameters)})
              AND DocumentType=N'SalesInvoice'
            ORDER BY ProcessingSequence;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var expectedBefore = quantityBefore;
        var count = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal(expectedBefore, reader.GetDecimal(0));
            Assert.Equal(-1m, reader.GetDecimal(1));
            expectedBefore -= 1m;
            Assert.Equal(expectedBefore, reader.GetDecimal(2));
            count++;
        }
        Assert.Equal(10, count);
    }

    private async Task<decimal> InventoryQuantityAsync() =>
        await ScalarAsync<decimal>("""
            SELECT QuantityOnHand FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
              AND ProductId=@ProductId;
            """, fixture.BusinessId, fixture.WarehouseId, fixture.ProductId);

    private async Task<ProductDailyTotal> ProductDailyTotalAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COALESCE(SUM(DocumentCount),0),COALESCE(SUM(Quantity),0),
                   COALESCE(SUM(NetTotalSales),0)
            FROM reporting.SalesReportDailyDimensionTotals
            WHERE BusinessId=@BusinessId AND BusinessLocalDate='2026-07-27'
              AND DimensionType=N'Product' AND DimensionKey=@ProductId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ProductDailyTotal(
            reader.GetInt64(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid businessId,
        Guid warehouseId, Guid productId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@ProductId", productId);
        var value = (await command.ExecuteScalarAsync())!;
        return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
    }

    private sealed record PipelineSnapshot(
        long OperationalJobs, long InventoryMovements, decimal InventoryQuantityChange,
        decimal InventoryQuantityAfter, long AccountingJobs, long AccountingEntries,
        long UnbalancedEntries, decimal CashDebit, decimal SalesRevenueCredit,
        decimal OutputVatCredit, long ReportDocuments, decimal ReportDocumentTotal,
        long ReportLineFacts, decimal ReportQuantity, decimal ReportLineTotal,
        long MaxOperationalMicroseconds, long MaxAccountingMicroseconds,
        long MaxReportingMicroseconds);

    private sealed record AccumulatedAccountingSnapshot(
        long AccountingSources, long AccountingJobs, long AccountingEntries,
        long UnbalancedEntries, long OperationalJobs, long InventoryMovements,
        long ReportingJobs, long ReportLineFacts, decimal ReceivablesOutstanding,
        decimal AccountsReceivableDebit, decimal AccountsReceivableCredit,
        decimal SalesRevenueCredit, decimal OutputVatDebit, decimal OutputVatCredit,
        decimal SalesReturnsDebit, decimal InventoryDebit, decimal InventoryCredit,
        decimal CostOfGoodsSoldDebit, decimal CostOfGoodsSoldCredit,
        decimal TotalDebits, decimal TotalCredits,
        decimal ReportSalesTotal, decimal ReportReturnedTotal,
        decimal ReportNetLineTotal);

    private sealed record ProductDailyTotal(
        long DocumentCount, decimal Quantity, decimal NetTotalSales);

    private async Task DisableAccountingAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.AccountingTenantSettings
            SET Status=N'Disabled', EffectiveFrom=NULL, OpeningBalanceMode=NULL,
                ActivatedAt=NULL, ActivatedByUserId=NULL, UpdatedAt=SYSDATETIMEOFFSET()
            WHERE TenantId=@TenantId;
            """;
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Accounting_endpoints_enforce_permission_and_scope()
    {
        using var denied = fixture.CreateAdminClient();
        using var response = await denied.GetAsync("/api/commerce/v1/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var manual = await denied.PostAsJsonAsync(
            "/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(
                Guid.NewGuid(), fixture.BusinessId, DateTimeOffset.UtcNow,
                "TEST", "Sin permiso", []));
        Assert.Equal(HttpStatusCode.Forbidden, manual.StatusCode);
        using var configured = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        using var wrongScope = await configured.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(Guid.NewGuid(), Guid.NewGuid(), "9999", "Fuera de alcance", "Asset", true, false));
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);
    }

    private async Task ConfigureAsync(HttpClient client)
    {
        var accounts = new Dictionary<string, (string Code, string Name, string Type)>(StringComparer.Ordinal)
        {
            [AccountingCategories.Cash] = ("110505", "Caja general", "Asset"),
            [AccountingCategories.DebitCardClearing] = ("111005", "Tarjetas debito por cobrar", "Asset"),
            [AccountingCategories.Bank] = ("111020", "Bancos", "Asset"),
            [AccountingCategories.OtherIncome] = ("429595", "Otros ingresos de caja", "Revenue"),
            [AccountingCategories.CashOverageIncome] = ("429596", "Sobrantes de caja", "Revenue"),
            [AccountingCategories.OwnerContributions] = ("311505", "Aportes del propietario", "Equity"),
            [AccountingCategories.OperatingExpense] = ("519510", "Gastos operativos", "Expense"),
            [AccountingCategories.OtherExpense] = ("539595", "Otras salidas de caja", "Expense"),
            [AccountingCategories.CashShortageExpense] = ("539596", "Faltantes de caja", "Expense"),
            [AccountingCategories.CreditCardClearing] = ("111010", "Tarjetas credito por cobrar", "Asset"),
            [AccountingCategories.TransferClearing] = ("111015", "Transferencias por conciliar", "Asset"),
            [AccountingCategories.AccountsReceivable] = ("130505", "Clientes", "Asset"),
            [AccountingCategories.AccountsPayable] = ("220505", "Proveedores", "Liability"),
            [AccountingCategories.SupplierCreditsReceivable] = ("133595", "Saldos a favor con proveedores", "Asset"),
            [AccountingCategories.InputVat] = ("240810", "IVA descontable", "Asset"),
            [AccountingCategories.PurchasesExpense] = ("519595", "Compras no inventariables", "Expense"),
            [AccountingCategories.SalesRevenue] = ("413595", "Ingresos por ventas", "Revenue"),
            [AccountingCategories.SalesReturns] = ("417595", "Devoluciones en ventas", "ContraRevenue"),
            [AccountingCategories.OutputVat] = ("240805", "IVA generado", "Liability"),
            [AccountingCategories.Inventory] = ("143505", "Inventarios", "Asset"),
            [AccountingCategories.CostOfGoodsSold] = ("613595", "Costo de ventas", "Expense"),
            [AccountingCategories.CustomerCreditsPayable] = ("238095", "Saldos a favor de clientes", "Liability"),
            [AccountingCategories.WithholdingIncomeTaxPayable] = ("236540", "Retencion en la fuente por pagar", "Liability"),
            [AccountingCategories.WithholdingVatPayable] = ("236701", "Retencion de IVA por pagar", "Liability"),
            [AccountingCategories.WithholdingIcaPayable] = ("236805", "Retencion de ICA por pagar", "Liability")
        };
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var item in accounts)
        {
            var id = Guid.NewGuid(); ids[item.Key] = id;
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
                new CreateAccountingAccountRequest(id, fixture.TenantId, item.Value.Code, item.Value.Name, item.Value.Type, true,
                    item.Key is AccountingCategories.AccountsReceivable
                        or AccountingCategories.AccountsPayable
                        or AccountingCategories.CustomerCreditsPayable));
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }
        var center = Guid.NewGuid();
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(center, fixture.BusinessId, "PRINCIPAL", "Operacion principal", null, true)))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var period = Guid.NewGuid();
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/periods",
            new CreateAccountingPeriodRequest(period, fixture.TenantId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "2026")))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        foreach (var item in ids)
        {
            using var response = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
                new SetAccountMappingRequest(fixture.TenantId, null, item.Key, item.Value, new DateOnly(2026, 1, 1), null));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
    }

    private async Task<Guid> CreateCashierAsync()
    {
        var userId = Guid.NewGuid();
        var username = $"accounting-cashier-{userId:N}";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               FirstName,LastName,IsActive,CreatedAt)
            VALUES
              (@UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
               N'Cajero',N'Contable',1,SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@RoleId", fixture.RoleId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@Email", $"{username}@test.local");
        await command.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task<Guid> CreateCustomerAsync()
    {
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,
               CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'Organization',N'Cliente contable',
                   N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers
              (CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,IsActive,
               CreatedBy,CreatedAt)
            VALUES(@CustomerId,@PartyId,@BusinessId,0,1,@UserId,SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
        return customerId;
    }

    private async Task AssertBalancedAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT e.DebitTotal,e.CreditTotal,SUM(l.Debit),SUM(l.Credit)
            FROM dbo.AccountingEntries e INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            WHERE e.SourceDocumentId=@Id GROUP BY e.DebitTotal,e.CreditTotal;
            """, connection); command.Parameters.AddWithValue("@Id", documentId); await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(1)); Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(2)); Assert.Equal(reader.GetDecimal(1), reader.GetDecimal(3));
    }

    private async Task AssertFastProcessingAsync(Guid documentId, string operation)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT DATEDIFF_BIG(microsecond,StartedAt,CompletedAt)
            FROM dbo.DocumentProcessingJobs
            WHERE DocumentId=@Id AND Status=N'Completed';
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        var microseconds = Convert.ToInt64(await command.ExecuteScalarAsync());
        Assert.True(microseconds < 2_000_000,
            $"El motor tardó {microseconds / 1000m:N0} ms en {operation}; el límite local es 2.000 ms.");
    }

    private PosSaleUploadRequest WithUblSnapshot(PosSaleUploadRequest request)
    {
        var address = new PosSaleUblAddressContract("11001", "Bogota", "Bogota D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract(ServerSliceFixture.SupplierTaxId, "7", "31", "1", "EMISOR HISTORICO", "EMISOR HISTORICO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract("222222222", "0", "13", "2", "CLIENTE HISTORICO", "CLIENTE HISTORICO", "R-99-PN", "ZZ", "No aplica", address);
        return request with
        {
            UblSnapshot = new PosSaleUblSnapshotContract(fixture.FiscalIssuerConfigurationId, "COP", "01", supplier, customer,
            new PosSaleUblAuthorizationContract(ServerSliceFixture.AuthorizationNumber, new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), ServerSliceFixture.Prefix, 1, 10000),
            "auraly-test-software", [new PosSaleUblLineContract(1, "P-E2E", "999", "EA", "IVA", 19m)], "1", "10", DateOnly.FromDateTime(request.FiscalSnapshot!.IssuedAt.Date), null)
        };
    }

    private static HttpRequestMessage CreateGoodsReceiptMessage(
        ConfirmGoodsReceiptRequest request,
        string idempotencyKey)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    private async Task ConfigureTargetMarginsAsync(Guid stockProductId, Guid nonStockProductId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.ProductPrices
            SET TargetMarginPercent=30,RoundingIncrement=1,RoundingMode=N'Nearest'
            WHERE BusinessId=@BusinessId AND ProductId IN (@StockProductId,@NonStockProductId)
              AND IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@StockProductId", stockProductId);
        command.Parameters.AddWithValue("@NonStockProductId", nonStockProductId);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private async Task<Guid> CreateNonStockProductAsync()
    {
        var productId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Products
              (ProductId,TenantId,BusinessId,Source,Sku,Name,Currency,
               ManageStock,IsActive,CreatedAt)
            VALUES
              (@ProductId,@TenantId,@BusinessId,0,@Sku,N'Servicio de compra',
               N'COP',0,1,SYSUTCDATETIME());

            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,
               ValidFrom,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,10000,N'COP','2026-01-01',1,SYSDATETIMEOFFSET());

            INSERT dbo.SupplierProducts
              (SupplierProductId,BusinessId,ProductId,SupplierId,
               SupplierProductCode,IsPrimary,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,@ProductId,@SupplierId,@Sku,1,1,
               SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@Sku", $"NS-{productId:N}");
        await command.ExecuteNonQueryAsync();
        return productId;
    }

    private async Task<decimal> AccountAmountAsync(
        Guid documentId,
        string accountCode,
        bool debit)
    {
        Assert.Contains(
            accountCode,
            new[] { "143505", "240810", "519595", "220505",
                "236540", "236701", "236805", "110505", "111005", "130505",
                "130510", "130515", "130520", "139995", "429595", "429596", "539595", "539596" });
        var column = debit ? "Debit" : "Credit";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"""
            SELECT COALESCE(SUM(l.[{column}]),0)
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.SourceDocumentId=@Id AND a.Code=@Code;
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@Code", accountCode);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<Guid> SeedDispatchSettlementAsync(
        Guid dispatchId,
        Guid sourceId,
        Guid settlementId,
        Guid salesDocumentId,
        decimal expectedCash)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Guid previousAccount;
        await using (var account = new SqlCommand("""
            SELECT TOP(1) mapping.AccountId
            FROM dbo.AccountingAccountMappings mapping
            WHERE mapping.TenantId=@TenantId
              AND mapping.Category=N'DispatchCashShortageExpense'
              AND mapping.EffectiveTo IS NULL
            ORDER BY CASE WHEN mapping.BusinessId=@BusinessId THEN 0 ELSE 1 END;
            """, connection))
        {
            account.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            account.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            previousAccount = (Guid)(await account.ExecuteScalarAsync()
                ?? throw new InvalidOperationException(
                    "The cash-shortage mapping was not provisioned."));
        }
        await using var command = new SqlCommand("""
            SET XACT_ABORT ON; BEGIN TRAN;
            INSERT dbo.Dispatches(
              DispatchId,TenantId,BusinessId,WarehouseId,DispatchNumber,ScheduledDate,
              DriverUserId,DriverName,Status,CreatedBy,CreatedAt,UpdatedAt)
            VALUES(
              @DispatchId,@TenantId,@BusinessId,@WarehouseId,@DispatchNumber,
              CONVERT(date,SYSUTCDATETIME()),@UserId,N'Transportador prueba',
              N'PendingSettlement',@UserId,SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.DispatchSourceDocuments(
              DispatchSourceDocumentId,DispatchId,SourceDocumentId,SourceDocumentType,
              DocumentNumberSnapshot,CustomerNameSnapshot,SellerNameSnapshot,
              DocumentTotalSnapshot,Status,CreatedAt)
            VALUES(
              @SourceId,@DispatchId,@SalesDocumentId,N'SalesInvoice',N'FACTURA-PRUEBA',
              N'Cliente prueba',N'Vendedor prueba',@Expected,N'Delivered',SYSUTCDATETIME());
            INSERT dbo.DispatchDeliveryPayments(
              DispatchDeliveryPaymentId,BusinessId,DispatchId,DispatchSourceDocumentId,
              ApplicationType,PaymentMethod,Amount,RecordedBy,OccurredAt,CreatedAt)
            VALUES(
              NEWID(),@BusinessId,@DispatchId,@SourceId,N'InvoicePayment',N'Cash',
              @Expected,@UserId,SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.DispatchSettlements(
              DispatchSettlementId,BusinessId,DispatchId,ExpectedCash,DeclaredCash,
              DepositTotal,CreditDocumentTotal,CreditAdvanceTotal,ReturnTotal,
              TransporterClosedBy,TransporterClosedAt,Status,IdempotencyKey)
            VALUES(
              @SettlementId,@BusinessId,@DispatchId,@Expected,@Expected,0,0,0,0,
              @UserId,SYSUTCDATETIME(),N'PendingReview',@CloseKey);
            UPDATE mapping
            SET AccountId=custom.AccountId
            FROM dbo.AccountingAccountMappings mapping
            CROSS APPLY(
              SELECT TOP(1) AccountId FROM dbo.AccountingAccounts
              WHERE TenantId=@TenantId AND Code=N'539595' AND IsActive=1
            ) custom
            WHERE mapping.TenantId=@TenantId
              AND mapping.Category=N'DispatchCashShortageExpense'
              AND mapping.EffectiveTo IS NULL
              AND (mapping.BusinessId=@BusinessId OR mapping.BusinessId IS NULL);
            COMMIT;
            """, connection);
        command.Parameters.AddWithValue("@DispatchId", dispatchId);
        command.Parameters.AddWithValue("@SourceId", sourceId);
        command.Parameters.AddWithValue("@SettlementId", settlementId);
        command.Parameters.AddWithValue("@SalesDocumentId", salesDocumentId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@DispatchNumber", $"DSP-{dispatchId:N}"[..20]);
        command.Parameters.AddWithValue("@CloseKey", $"close-{dispatchId:N}");
        var money = command.Parameters.Add("@Expected", System.Data.SqlDbType.Decimal);
        money.Precision = 19;
        money.Scale = 4;
        money.Value = expectedCash;
        await command.ExecuteNonQueryAsync();
        return previousAccount;
    }

    private async Task RestoreShortageAccountAsync(Guid accountId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.AccountingAccountMappings SET AccountId=@AccountId
            WHERE TenantId=@TenantId AND Category=N'DispatchCashShortageExpense'
              AND EffectiveTo IS NULL
              AND (BusinessId=@BusinessId OR BusinessId IS NULL);
            """, connection);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> PostingStatusAsync(Guid documentId, string documentType)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Status FROM dbo.AccountingPostingJobs
            WHERE SourceDocumentId=@Id AND SourceDocumentType=@Type;
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@Type", documentType);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task WaitForDispatchAccountingAsync(Guid settlementId, Guid dispatchId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await PostingStatusAsync(settlementId,
                    DispatchAccountingDocumentTypes.CashDifference) ==
                AccountingPostingStatuses.Posted &&
                await ScalarAsync<string>(
                    "SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id",
                    dispatchId) == "Closed") return;
            await Task.Delay(50);
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT operation.Status,operation.Attempts,operation.LastError,
                   settlement.Status,dispatch.Status,
                   processing.Status,processing.LastError,
                   posting.Status,posting.LastErrorCode,posting.LastErrorMessage
            FROM dbo.DispatchSettlementOperations operation
            INNER JOIN dbo.DispatchSettlements settlement ON settlement.DispatchId=operation.DispatchId
            INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=operation.DispatchId
            LEFT JOIN dbo.DocumentProcessingJobs processing
              ON processing.DocumentId=settlement.DispatchSettlementId
             AND processing.DocumentType=N'DispatchCashDifference'
            LEFT JOIN dbo.AccountingPostingJobs posting
              ON posting.SourceDocumentId=settlement.DispatchSettlementId
             AND posting.SourceDocumentType=N'DispatchCashDifference'
            WHERE operation.DispatchId=@DispatchId;
            """, connection);
        command.Parameters.AddWithValue("@DispatchId", dispatchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The dispatch settlement operation is missing.");
        var values = Enumerable.Range(0, reader.FieldCount)
            .Select(index => reader.IsDBNull(index) ? "<null>" : Convert.ToString(reader.GetValue(index)))
            .ToArray();
        Assert.Fail($"Dispatch accounting did not complete: {string.Join(" | ", values)}");
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    { await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync(); await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@Id", id); var value=(await command.ExecuteScalarAsync())!; return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T)); }
    private async Task SetWarehouseNegativeSalesPolicyAsync(bool value)
    { await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync(); await using var command = new SqlCommand("UPDATE dbo.Warehouses SET AllowNegativeStockSales=@Value WHERE WarehouseId=@Id", connection); command.Parameters.AddWithValue("@Value", value); command.Parameters.AddWithValue("@Id", fixture.WarehouseId); Assert.Equal(1, await command.ExecuteNonQueryAsync()); }
    private async Task<int> CountAsync(string table, string column, Guid id)
    { Assert.Contains($"{table}:{column}", new[] { "AccountingEntries:SourceDocumentId", "AccountingPostingJobs:SourceDocumentId", "AccountingSourceDocuments:SourceDocumentId", "DocumentProcessingJobs:DocumentId" }); await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync(); await using var command = new SqlCommand($"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id", connection); command.Parameters.AddWithValue("@Id", id); return Convert.ToInt32(await command.ExecuteScalarAsync()); }
}
