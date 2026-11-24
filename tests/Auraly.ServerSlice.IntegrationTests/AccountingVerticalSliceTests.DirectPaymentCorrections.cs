using System.Net;
using System.Net.Http.Json;
using Auraly.Application.WorkSessions;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Invoice_cash_form_correction_posts_only_when_the_auxiliary_changes(bool sameForm) =>
        Invoice_payment_correction_reuses_financial_engine_and_is_not_reapplied_at_closure("Cash", false, false, true, sameForm);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Invoice_direct_correction_updates_reporting_before_the_closure(bool credit, bool correctionFirst) =>
        Invoice_payment_correction_reuses_financial_engine_and_is_not_reapplied_at_closure("Transfer", true, credit, true, false, true, correctionFirst);

    [Theory]
    [InlineData("Transfer", false, false)]
    [InlineData("Transfer", true, false)]
    [InlineData("DebitCard", true, false)]
    [InlineData("CreditCard", false, false)]
    [InlineData("Cash", false, true)]
    [InlineData("Transfer", true, true)]
    [InlineData("Transfer", false, false, false)]
    [InlineData("DebitCard", true, true, false)]
    public async Task Invoice_payment_correction_reuses_financial_engine_and_is_not_reapplied_at_closure(
        string method, bool countedRealMethod, bool credit, bool accounting = true, bool sameForm = false, bool verifyReporting = false, bool correctionFirst = false)
    {
        using var admin = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, CatalogPermissionCodes.Update);
        using (var defaults = await admin.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        if (accounting)
        {
            using var activation = await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
                new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"));
            activation.EnsureSuccessStatusCode();
        }
        else await DisableAccountingAsync();
        var original = await CreateClosureFormAsync(admin, "Cash");
        var target = sameForm ? original : await CreateClosureFormAsync(admin, method);
        var userId = await fixture.CreateWorkSessionUserAsync("invoice-correction");
        var identity = new WorkSessionIdentity(userId, fixture.TenantId,
            new HashSet<string> { WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close,
                WorkSessionPermissionCodes.ReadCashDifferences, WorkSessionPermissionCodes.ReconcileClosures,
                CommercePermissionCodes.SalesCorrectPaymentMethods }, fixture.BusinessId);
        using var scope = fixture.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<WorkSessionService>();
        var sessionId = Guid.NewGuid();
        await sessions.RegisterDeviceOpenAsync(identity,
            new(userId, sessionId, fixture.BusinessId, new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)), fixture.DeviceId);
        var sale = fixture.CreateUniqueValidRequest() with {
            WorkSessionId = sessionId, SoldByUserId = userId,
            Payments = [new(1, "Cash", 11900, null, PaymentMethodId: original.PaymentMethodId,
                PaymentMethodVersion: original.Version, PaymentMethodCode: original.Code,
                PaymentMethodName: original.Name, AccountId: original.AccountId)] };
        if (credit)
        {
            var (customer, site) = await CreateCustomerAsync();
            sale = sale with { CustomerId = customer, CustomerPartySiteId = site, Payments = [],
                Credit = new(customer, 11900, new(2026, 8, 31, 0, 0, 0, TimeSpan.FromHours(-5)), PartySiteId: site) };
        }
        sale = await AsDirectCorrectionReceiptAsync(sale);
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        if (accounting) await AssertBalancedAsync(sale.DocumentId);
        var reporting = scope.ServiceProvider.GetRequiredService<Auraly.Infrastructure.Persistence.SqlSalesReportingProcessor>();
        if (verifyReporting && !correctionFirst)
        {
            await reporting.ProcessAsync(sale.DocumentId, "SalesReceipt", fixture.BusinessId, 1, default);
            Assert.Equal(credit ? 11900m : 0m, await ScalarAsync<decimal>("SELECT CreditAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
        }
        var originalPayments = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@Id", sale.DocumentId);
        var group = method is "DebitCard" or "CreditCard" ? "Card" : method;
        var cash = countedRealMethod ? group == "Cash" ? 11900m : 0m : credit ? 0m : 11900m;
        var closure = await sessions.CloseFromDeviceAsync(identity, sessionId, Guid.NewGuid().ToString("N"),
            new(cash, null, fixture.UserId, [new("Cash", cash),
                new("Transfer", countedRealMethod && group == "Transfer" ? 11900 : 0),
                new("Card", countedRealMethod && group == "Card" ? 11900 : 0)]));
        var path = $"/api/commerce/v1/work-sessions/sales/{sale.DocumentId}/payment-correction";
        using var reviewer = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCorrectPaymentMethods);
        using var cashier = fixture.CreateUserClient(userId, WorkSessionPermissionCodes.ReadCashDifferences);
        using (var forbidden = await cashier.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var context = (await reviewer.GetFromJsonAsync<SalePaymentCorrectionContext>(path))!;
        using (var commands = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnection(fixture.ConnectionString).Database))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await sessions.GetSalePaymentCorrectionAsync(identity, sale.DocumentId);
            watch.Stop(); output.WriteLine("Invoice correction context: {0} SQL commands, {1:F1} ms", commands.Count, watch.Elapsed.TotalMilliseconds);
            Assert.Equal(2, commands.Count);
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(750), watch.Elapsed.ToString());
        }
        var item = Assert.Single(context.Items);
        Assert.Equal(credit ? "CreditSale" : "Sale", item.MovementType);
        var franchise = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code;
        var correction = new WorkSessionPaymentCorrection(item.VerificationKey, group, 11900, "Recaudo real desde Facturas",
            method, group == "Card" ? franchise : null, group == "Card" ? "DIRECT-APPROVAL" : null,
            method == "Transfer" ? "DIRECT-REFERENCE" : null, target.PaymentMethodId, target.Version);
        var request = new CorrectSalePaymentRequest(Guid.NewGuid(), correction);
        using (var forbidden = await cashier.PostAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await Assert.ThrowsAsync<WorkSessionValidationException>(() => sessions.CorrectSalePaymentAsync(identity,
            sale.DocumentId, request with { Correction = correction with { Amount = 11901 } }));
        using (var invalid = await reviewer.PostAsJsonAsync(path, request with { Correction = correction with { Amount = 11901 } }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var accepted = await reviewer.PostAsJsonAsync(path, request);
        if (!accepted.IsSuccessStatusCode)
            await sessions.CorrectSalePaymentAsync(identity, sale.DocumentId, request);
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        var result = (await accepted.Content.ReadFromJsonAsync<SalePaymentCorrectionAcceptance>())!;
        Assert.Equal(request.CorrectionId, result.CorrectionId);
        using (var pendingReplay = await reviewer.PostAsJsonAsync(path, request))
        {
            Assert.True(pendingReplay.IsSuccessStatusCode, await pendingReplay.Content.ReadAsStringAsync());
            Assert.True((await pendingReplay.Content.ReadFromJsonAsync<SalePaymentCorrectionAcceptance>())!.IdempotentReplay);
        }
        using (var concurrent = await reviewer.PostAsJsonAsync(path, request with { CorrectionId = Guid.NewGuid() }))
            Assert.Equal(HttpStatusCode.Conflict, concurrent.StatusCode);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionClosureReconciliations WHERE WorkSessionClosureId=@Id", closure.WorkSessionClosureId));
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var blindPostingRequired = accounting && countedRealMethod;
        Assert.Equal(blindPostingRequired ? 1 : 0, await CountAsync("AccountingSourceDocuments", "SourceDocumentId", closure.WorkSessionClosureId));
        if (blindPostingRequired)
            await processor.ProcessAsync(closure.WorkSessionClosureId, WorkSessionAccountingDocumentTypes.CashDifference, fixture.BusinessId, default);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await processor.ProcessAsync(result.CorrectionId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);
            using var replay = await reviewer.PostAsJsonAsync(path, request);
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<SalePaymentCorrectionAcceptance>())!.IdempotentReplay);
        }
        Assert.Equal(accounting && !sameForm ? "Posted" : "CommercialEffectsApplied", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'WorkSessionClosureReconciliation'", result.CorrectionId));
        if (accounting && !sameForm) await AssertBalancedAsync(result.CorrectionId);
        Assert.Equal(accounting ? 11900m : 0m, await ScalarAsync<decimal>("SELECT COALESCE(SUM(Debit-Credit),0) FROM dbo.AccountingEntryLines WHERE AccountId=@Id", target.AccountId));
        if (!credit && !sameForm) Assert.Equal(0m, await ScalarAsync<decimal>("SELECT COALESCE(SUM(Debit-Credit),0) FROM dbo.AccountingEntryLines WHERE AccountId=@Id", original.AccountId));
        Assert.Equal(originalPayments, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@Id", sale.DocumentId));
        if (credit) Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id", sale.DocumentId));
        var correctedContext=Assert.Single((await sessions.GetSalePaymentCorrectionAsync(identity,sale.DocumentId)).Items);
        Assert.Equal(method,correctedContext.CorrectedTenderMethodCode);
        Assert.Equal(11900m,correctedContext.CorrectedAmount);
        Assert.Equal(target.Name,correctedContext.CorrectedPaymentMethodName);
        if (verifyReporting)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (correctionFirst && attempt == 1)
                    await reporting.ProcessAsync(sale.DocumentId, "SalesReceipt", fixture.BusinessId, 1, default);
                await reporting.ProcessAsync(result.CorrectionId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, 1, default);
            }
            Assert.Equal(method, await ScalarAsync<string>("SELECT MethodCode FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'Payment'", sale.DocumentId));
            Assert.Equal(11900m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'Payment'", sale.DocumentId));
            Assert.Equal(11900m, await ScalarAsync<decimal>("SELECT TotalAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
            Assert.Equal(11900m, await ScalarAsync<decimal>("SELECT CollectedAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT CreditAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
        }
        using (var changedReplay = await reviewer.PostAsJsonAsync(path, request with { Correction = correction with { Reason = "Otro motivo" } }))
            Assert.Equal(HttpStatusCode.Conflict, changedReplay.StatusCode);
        var effective = await sessions.ListClosurePaymentVerificationPageAsync(identity, closure.WorkSessionClosureId,
            null, null, 1, 100, default);
        var applied = Assert.Single(effective.Items, row => row.SourceId == sale.DocumentId);
        Assert.Equal(result.CorrectionId, applied.AppliedCorrectionId);
        Assert.Equal(group, applied.PaymentMethodCode);
        var final = await sessions.ReconcileClosureAsync(identity, closure.WorkSessionClosureId, Guid.NewGuid().ToString("N"),
            new(closure.PaymentTotals.Where(total => total.RequiresCount)
                .Select(total => new ReconcileWorkSessionClosureLine(total.PaymentMethodCode,
                    total.PaymentMethodCode == group ? 11900 : 0, true, null)).ToArray(), [], "Cierre después de corrección individual",
                group == "Cash" ? [] : [new(applied.VerificationKey, "Verified")], []));
        var finalPostingRequired = accounting && !countedRealMethod && (credit || group != "Cash");
        Assert.Equal(finalPostingRequired ? "Pending" : "NotRequired", final.AccountingStatus);
        if (finalPostingRequired)
        {
            await processor.ProcessAsync(final.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);
            Assert.Equal("CommercialEffectsApplied", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'WorkSessionClosureReconciliation'", final.ReconciliationId));
        }
        else Assert.Equal(0, await CountAsync("AccountingSourceDocuments", "SourceDocumentId", final.ReconciliationId));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT COALESCE(SUM(line.Debit+line.Credit),0) FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId WHERE entry.SourceDocumentId=@Id", final.ReconciliationId));
        Assert.Equal(accounting && !sameForm ? 1 : 0, await CountAsync("AccountingEntries", "SourceDocumentId", result.CorrectionId));
        Assert.Equal(accounting ? 11900m : 0m, await ScalarAsync<decimal>("SELECT COALESCE(SUM(Debit-Credit),0) FROM dbo.AccountingEntryLines WHERE AccountId=@Id", target.AccountId));
        if (credit) Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceivableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Adjustment'", result.CorrectionId));
        using var finalReplay = await reviewer.PostAsJsonAsync(path, request);
        Assert.True(finalReplay.IsSuccessStatusCode, await finalReplay.Content.ReadAsStringAsync());
        Assert.True((await finalReplay.Content.ReadFromJsonAsync<SalePaymentCorrectionAcceptance>())!.IdempotentReplay);
    }

    private async Task<PosSaleUploadRequest> AsDirectCorrectionReceiptAsync(PosSaleUploadRequest sale)
    {
        var series = Guid.NewGuid();
        var code = $"DI{sale.DocumentNumber.Consecutive}";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            VALUES(@Id,@Business,@Device,N'SalesReceipt',N'CVI',@Code,8,1,99999999,1,1,SYSDATETIMEOFFSET());
            """, connection);
        seed.Parameters.AddWithValue("@Id", series); seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
        seed.Parameters.AddWithValue("@Device", fixture.DeviceId); seed.Parameters.AddWithValue("@Code", code);
        await seed.ExecuteNonQueryAsync();
        return sale with { DocumentNumber = new(series, PosSaleDocumentTypes.Receipt, "CVI", code,
                sale.DocumentNumber.Consecutive, 8, $"CVI{code}-{sale.DocumentNumber.Consecutive:D8}"),
            CommercialSnapshot = sale.CommercialSnapshot with { DocumentType = PosSaleDocumentTypes.Receipt },
            FiscalSnapshot = null, UblSnapshot = null };
    }
}
