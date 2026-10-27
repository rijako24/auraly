using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Auraly.Application.Expenses;
using Auraly.Commerce.Accounting.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ExpenseCaptureTests
{
    [Theory]
    [InlineData("Increase")]
    [InlineData("Decrease")]
    [InlineData("CreditApplication")]
    [InlineData("Unapplication")]
    public async Task Accepted_expense_cancellation_reserves_its_obligation_until_the_same_processor_finishes(string direction)
    {
        using var client = fixture.CreateAdminClient(ExpensePermissionCodes.Read, ExpensePermissionCodes.Create,
            ExpensePermissionCodes.Cancel, TaxationPermissionCodes.ManageWithholdingRules,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            PayablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        using (var profile = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
            new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, false, [], "11001"))) profile.EnsureSuccessStatusCode();
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5196", "Expense", "General", "Debit");
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var expense = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId, null,
            null, Guid.NewGuid().ToString("N"), at, at.AddDays(30), "COP", "Gasto con anulación aceptada",
            0, 0, null, null, Lines: [new(account.AccountId, null, null, "Servicio", 1_000m, null,
                PurchasingTaxTreatments.CapitalizedCost, null)]);
        using var previewResponse = await client.PostAsJsonAsync("/api/commerce/v1/expenses/preview", expense);
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ExpensePreview>())!;
        using (var confirmed = await SendAsync(client, expense with { CalculationHash = preview.CalculationHash }))
            Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
        var detail = (await client.GetFromJsonAsync<ExpenseDetail>($"/api/commerce/v1/expenses/{expense.ExpenseId}"))!;
        var payableId = detail.Payable!.PayableId;
        var outstanding = 1_000m;
        Guid? originalTransactionId = null;
        VoucherDraftView? competingDraft = null;
        if (direction is "CreditApplication" or "Unapplication")
        {
            var position = Assert.Single((await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={payableId}&page=1&pageSize=1"))!.Items);
            var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                "1396", "Asset", "SupplierAdvance", "Debit");
            var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
            var isApplication = direction == "CreditApplication";
            var money = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1),
                "MANUAL_VOUCHER", "Dinero anterior a la anulación", "PAGO-ANTES-ANULACION",
                [new(isApplication ? advance.AccountId : position.AccountId, fixture.SupplierPartyId, null,
                    "Destino del dinero", 200m, 0, LineId: Guid.NewGuid(),
                    ExistingPosition: isApplication ? null : new("Payable", payableId)),
                 new(cash.AccountId, fixture.SupplierPartyId, null, "Salida real", 0, 200m, LineId: Guid.NewGuid())],
                null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Disbursement");
            var moneyDraft = await SaveAsync(money);
            using var moneyResponse = await client.PostAsJsonAsync(DraftPath(money.DocumentId) + "/send", new SendVoucherDraftRequest(moneyDraft.RowVersion));
            Assert.True(moneyResponse.IsSuccessStatusCode, await moneyResponse.Content.ReadAsStringAsync());
            var applied = (await moneyResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Guid? creditId = isApplication ? Assert.Single(applied.FinancialEffects!.Positions).PositionId : null;
            if (!isApplication)
            {
                outstanding -= 200m;
                await using var paymentConnection = new SqlConnection(fixture.ConnectionString);
                await paymentConnection.OpenAsync();
                await using var payment = new SqlCommand("SELECT PayableTransactionId FROM dbo.PayableTransactions WHERE PayableId=@Id AND TransactionType=N'Payment';", paymentConnection);
                payment.Parameters.AddWithValue("@Id", payableId);
                originalTransactionId = (Guid)(await payment.ExecuteScalarAsync())!;
                using var capture = await client.GetAsync(CapturePath());
                Assert.True(capture.IsSuccessStatusCode, await capture.Content.ReadAsStringAsync());
            }
            competingDraft = await SaveAsync(money with { DocumentId = Guid.NewGuid(), VoucherTypeCode = "Accounting",
                OccurredAt = DateTimeOffset.UtcNow,
                Lines = isApplication
                    ? [new(position.AccountId, fixture.SupplierPartyId, null, "Cruzar obligación", 1m, 0,
                           LineId: Guid.NewGuid(), ExistingPosition: new("Payable", payableId)),
                       new(advance.AccountId, fixture.SupplierPartyId, null, "Usar saldo a favor", 0, 1m,
                           LineId: Guid.NewGuid(), ExistingPosition: new("SupplierCredit", creditId!.Value))]
                    : [new(advance.AccountId, fixture.SupplierPartyId, null, "Conservar dinero", 1m, 0, LineId: Guid.NewGuid()),
                       new(position.AccountId, fixture.SupplierPartyId, null, "Reabrir obligación", 0, 1m,
                           LineId: Guid.NewGuid(), ExistingPosition: new("Payable", payableId, originalTransactionId))] });
        }
        using var scope = fixture.CreateScope();
        // Exercise the real durable acceptance before its signal is delivered.
        // No synthetic status or second cancellation writer is involved.
        var cancellation = await scope.ServiceProvider.GetRequiredService<IExpenseStore>().CancelAsync(
            new(fixture.UserId, fixture.TenantId, fixture.BusinessId, new HashSet<string> { ExpensePermissionCodes.Cancel }),
            expense.ExpenseId, new(Guid.NewGuid(), "Anular gasto antes de otro movimiento"), default);
        var adjustment = new ConfirmAccountAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId, "Payable", payableId,
            direction, 1m, account.AccountId, null, DateTimeOffset.UtcNow, "ACCOUNT_ADJUSTMENT", "Movimiento competidor");
        using var response = competingDraft is null
            ? await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/account-adjustments", adjustment)
            : await client.PostAsJsonAsync(DraftPath(competingDraft.DocumentId) + "/send", new SendVoucherDraftRequest(competingDraft.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("anulación", await response.Content.ReadAsStringAsync());
        if (originalTransactionId is not null)
        {
            using var capture = await client.GetAsync(CapturePath());
            Assert.Equal(HttpStatusCode.Conflict, capture.StatusCode);
            Assert.Contains("anulación", await capture.Content.ReadAsStringAsync());
        }
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var state = new SqlCommand("""
            SELECT payable.OutstandingAmount,available.AvailableAmount,available.PendingCancellationCount,
              (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@AdjustmentId)
            FROM dbo.Payables payable
            CROSS APPLY accounting.ObligationAvailability(payable.BusinessId,N'Payable',payable.PayableId,payable.OutstandingAmount,NULL) available
            WHERE payable.PayableId=@Id AND payable.BusinessId=@BusinessId;
            """, connection))
        {
            state.Parameters.AddWithValue("@Id", payableId);
            state.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            state.Parameters.AddWithValue("@AdjustmentId", competingDraft?.DocumentId ?? adjustment.AdjustmentId);
            await using var reader = await state.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(outstanding, reader.GetDecimal(0));
            Assert.Equal(0m, reader.GetDecimal(1));
            Assert.Equal(1, reader.GetInt32(2));
            Assert.Equal(0, reader.GetInt32(3));
        }
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        await processor.ProcessAsync(cancellation.CancellationId, ExpenseDocumentTypes.Cancellation, fixture.BusinessId, default);
        var cancelled = (await client.GetFromJsonAsync<ExpenseDetail>($"/api/commerce/v1/expenses/{expense.ExpenseId}"))!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(0m, cancelled.Payable!.OutstandingAmount);
        if (originalTransactionId is not null)
        {
            // A paid/partially paid obligation keeps its historical Paid status
            // after cancellation. That must not permit reopening its void source.
            using var capture = await client.GetAsync(CapturePath());
            Assert.Equal(HttpStatusCode.Conflict, capture.StatusCode);
            Assert.Contains("anulado", await capture.Content.ReadAsStringAsync());
            using var retry = await client.PostAsJsonAsync(DraftPath(competingDraft!.DocumentId) + "/send",
                new SendVoucherDraftRequest(competingDraft.RowVersion));
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
            Assert.Contains("anulado", await retry.Content.ReadAsStringAsync());
            using var increase = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/account-adjustments",
                adjustment with { Direction = "Increase" });
            Assert.Equal(HttpStatusCode.Conflict, increase.StatusCode);
            Assert.Contains("anulado", await increase.Content.ReadAsStringAsync());
        }

        string DraftPath(Guid id) => $"/api/commerce/v1/accounting/manual/drafts/{id}";
        string CapturePath() => $"/api/commerce/v1/accounting/unapplication-capture?subledgerKind=Payable&subledgerId={payableId}&transactionId={originalTransactionId}";
        async Task<VoucherDraftView> SaveAsync(SaveVoucherDraftRequest capture)
        {
            using var response = await client.PutAsJsonAsync(DraftPath(capture.DocumentId), capture);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(false, 200)]
    [InlineData(true, 400)]
    [InlineData(true, 200)]
    public async Task Cancellation_after_unapplication_preserves_the_real_money_and_original_credit(
        bool creditApplication, decimal restoredAmount)
    {
        using var client = fixture.CreateAdminClient(ExpensePermissionCodes.Read, ExpensePermissionCodes.Create,
            ExpensePermissionCodes.Cancel, TaxationPermissionCodes.ManageWithholdingRules,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            PayablesPermissionCodes.Read, PayablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        using (var profile = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
            new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, false, [], "11001"))) profile.EnsureSuccessStatusCode();
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5196", "Expense", "General", "Debit");
        var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "1396", "Asset", "SupplierAdvance", "Debit");
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var expense = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId, null,
            null, Guid.NewGuid().ToString("N"), at, at.AddDays(30), "COP", "Servicio con aplicación restituida",
            0, 0, null, null, Lines: [new(account.AccountId, null, null, "Servicio", 1_000m, null,
                PurchasingTaxTreatments.CapitalizedCost, null)]);
        using var previewResponse = await client.PostAsJsonAsync("/api/commerce/v1/expenses/preview", expense);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ExpensePreview>())!;
        using (var confirmed = await SendAsync(client, expense with { CalculationHash = preview.CalculationHash }))
            Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
        var detail = (await client.GetFromJsonAsync<ExpenseDetail>($"/api/commerce/v1/expenses/{expense.ExpenseId}"))!;
        var payableId = detail.Payable!.PayableId;
        var page = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={payableId}&page=1&pageSize=1"))!;
        var position = Assert.Single(page.Items);
        var sessionId = await fixture.OpenWebWorkSessionAsync();
        var moneyCapture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1),
            "MANUAL_VOUCHER", "Dinero real que se conserva", "PAGO-GASTO",
            [new(creditApplication ? advance.AccountId : position.AccountId, fixture.SupplierPartyId, null,
                "Destino del dinero", 400m, 0, LineId: Guid.NewGuid(),
                ExistingPosition: creditApplication ? null : new("Payable", payableId)),
             new(cash.AccountId, fixture.SupplierPartyId, null, "Salida real", 0, 400m, LineId: Guid.NewGuid())],
            null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Disbursement", WorkSessionId: sessionId);
        var money = await SaveAndSendAsync(moneyCapture);
        Guid? originalCreditId = null;
        Guid appliedTransactionId;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        if (creditApplication)
        {
            originalCreditId = Assert.Single(money.FinancialEffects!.Positions).PositionId;
            var crossing = moneyCapture with { DocumentId = Guid.NewGuid(), VoucherTypeCode = "Accounting", WorkSessionId = null,
                Lines = [new(position.AccountId, fixture.SupplierPartyId, null, "Aplicar anticipo", 400m, 0,
                    LineId: Guid.NewGuid(), ExistingPosition: new("Payable", payableId)),
                 new(advance.AccountId, fixture.SupplierPartyId, null, "Crédito original", 0, 400m,
                    LineId: Guid.NewGuid(), ExistingPosition: new("SupplierCredit", originalCreditId.Value))] };
            appliedTransactionId = Assert.Single((await SaveAndSendAsync(crossing)).FinancialEffects!.CreditApplications).EffectId;
        }
        else
        {
            await using var original = new SqlCommand("SELECT PayableTransactionId FROM dbo.PayableTransactions WHERE PayableId=@Id AND TransactionType=N'Payment';", connection);
            original.Parameters.AddWithValue("@Id", payableId);
            appliedTransactionId = (Guid)(await original.ExecuteScalarAsync())!;
        }
        var restoration = moneyCapture with { DocumentId = Guid.NewGuid(), VoucherTypeCode = "Accounting", WorkSessionId = null,
            OccurredAt = at.AddDays(2), Description = "Restituir la aplicación conservando el dinero",
            Lines = [new(advance.AccountId, fixture.SupplierPartyId, null, "Anticipo conservado", restoredAmount, 0,
                LineId: Guid.NewGuid(), ExistingPosition: originalCreditId is { } creditId ? new("SupplierCredit", creditId) : null),
             new(position.AccountId, fixture.SupplierPartyId, null, "Reabrir obligación", 0, restoredAmount,
                LineId: Guid.NewGuid(), ExistingPosition: new("Payable", payableId, appliedTransactionId))] };
        var restored = await SaveAndSendAsync(restoration);
        var preservedCreditId = originalCreditId ?? Assert.Single(restored.FinancialEffects!.Positions).PositionId;
        var cancellation = new CancelExpenseRequest(Guid.NewGuid(), "Anular después de restituir aplicación");
        var cancelPath = $"/api/commerce/v1/expenses/{expense.ExpenseId}/cancel";
        using var validation = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("SELECT CONVERT(bigint,CASE WHEN EXISTS(SELECT 1 FROM dbo.Payables payable", StringComparison.Ordinal));
        var firstCancellation = true;
        var commandTimings = new List<(string Label, double Milliseconds)>();
        using var timings = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            afterExecute: (command, milliseconds) => {
                if (!firstCancellation) return;
                var label = command.CommandType == System.Data.CommandType.StoredProcedure ? command.CommandText
                    : command.CommandText.Contains("SELECT CONVERT(bigint,CASE WHEN EXISTS(SELECT 1 FROM dbo.Payables payable", StringComparison.Ordinal) ? "expense.cancel.validation"
                    : command.CommandText.Contains("SELECT e.Status,e.CancellationId", StringComparison.Ordinal) ? "expense.cancel.load"
                    : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(command.CommandText)))[..12];
                commandTimings.Add((label, milliseconds));
            });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (creditApplication && restoredAmount < 400m)
        {
            using var rejected = await client.PostAsJsonAsync(cancelPath, cancellation);
            timer.Stop();
            firstCancellation = false;
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal(0m, await ScalarAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", cancellation.CancellationId));
            Assert.Equal(600m + restoredAmount, await ScalarAsync("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        }
        else
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var cancelled = await client.PostAsJsonAsync(cancelPath, cancellation);
                if (attempt == 0) { timer.Stop(); firstCancellation = false; }
                Assert.True(cancelled.IsSuccessStatusCode, await cancelled.Content.ReadAsStringAsync());
            }
            var after = (await client.GetFromJsonAsync<ExpenseDetail>($"/api/commerce/v1/expenses/{expense.ExpenseId}"))!;
            Assert.Equal("Cancelled", after.Status);
            Assert.Equal(0m, after.Payable!.OutstandingAmount);
            Assert.Equal(creditApplication ? 0m : 400m - restoredAmount,
                await ScalarAsync("SELECT COALESCE(SUM(OriginalAmount),0) FROM dbo.SupplierCredits WHERE SourceDocumentId=@Id", cancellation.CancellationId));
            Assert.Equal(1m, await ScalarAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", cancellation.CancellationId));
        }
        Assert.Equal(restoredAmount, await ScalarAsync("SELECT AvailableAmount FROM dbo.SupplierCredits WHERE SupplierCreditId=@Id", preservedCreditId));
        Assert.Equal(400m, await ScalarAsync("SELECT SUM(TotalAmount) FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id", moneyCapture.DocumentId));
        Assert.Equal(-400m, await ScalarAsync("SELECT SUM(movement.Amount) FROM dbo.WorkSessionMovements movement JOIN dbo.SupplierPayments payment ON payment.PaymentId=movement.DocumentId WHERE payment.ParentSourceDocumentId=@Id", moneyCapture.DocumentId));
        Assert.Equal(0m, await ScalarAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", cancellation.CancellationId));
        Assert.Equal(1m, await ScalarAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", expense.ExpenseId));
        Assert.Equal(1, validation.Count);
        output.WriteLine("Anulación tras desaplicar {0} ({1}): una validación SQL, HTTP/motor {2:F1} ms.",
            restoredAmount, creditApplication ? "crédito" : "dinero", timer.Elapsed.TotalMilliseconds);
        output.WriteLine("First cancellation: SQL commands={0}, SQL total={1:F1} ms.", commandTimings.Count, commandTimings.Sum(item => item.Milliseconds));
        foreach (var timing in commandTimings.OrderByDescending(item => item.Milliseconds).Take(8))
            output.WriteLine("Cancellation command {0}: {1:F1} ms.", timing.Label, timing.Milliseconds);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"La anulación demoró {timer.Elapsed.TotalMilliseconds:F1} ms.");

        async Task<decimal> ScalarAsync(string sql, Guid id)
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);
            return Convert.ToDecimal(await command.ExecuteScalarAsync());
        }
        async Task<VoucherDraftView> SaveAndSendAsync(SaveVoucherDraftRequest capture)
        {
            var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
            using var saved = await client.PutAsJsonAsync(path, capture);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var draft = (await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var sent = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
            return (await sent.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
    }
}
