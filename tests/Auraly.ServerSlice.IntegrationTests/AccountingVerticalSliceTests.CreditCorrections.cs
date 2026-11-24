using System.Net;
using System.Net.Http.Json;
using Auraly.Application.WorkSessions;
using Auraly.Application.Receivables;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData("Cash", true, 0)]
    [InlineData("Transfer", true, 1900)]
    [InlineData("DebitCard", true, 0)]
    [InlineData("CreditCard", true, 0)]
    [InlineData("Cash", false, 0)]
    public async Task Credit_sale_correction_collects_only_remaining_debt_once_without_rewriting_the_invoice(
        string method, bool accounting, decimal priorPayment)
    {
        var settings = await CaptureOpeningTestSettingsAsync();
        try
        {
            using var admin = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, CatalogPermissionCodes.Update);
            using (var defaults = await admin.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
            if (accounting)
            {
                using var activation = await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate", new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"));
                activation.EnsureSuccessStatusCode();
            }
            else await DisableAccountingAsync();
            var form = await CreateClosureFormAsync(admin, method);
            var (customer, site) = await CreateCustomerAsync();
            using var scope = fixture.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<WorkSessionService>();
            var userId = await fixture.CreateWorkSessionUserAsync("credit-correction");
            var identity = new WorkSessionIdentity(userId, fixture.TenantId,
                new HashSet<string> { WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close,
                    WorkSessionPermissionCodes.ReadCashDifferences, WorkSessionPermissionCodes.ReconcileClosures,
                    CommercePermissionCodes.SalesCorrectPaymentMethods }, fixture.BusinessId);
            var sessionId = Guid.NewGuid();
            await sessions.RegisterDeviceOpenAsync(identity, new(userId, sessionId, fixture.BusinessId,
                new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)), fixture.DeviceId);
            var sale = WithUblSnapshot(fixture.CreateUniqueValidRequest() with {
                CustomerId = customer, CustomerPartySiteId = site, SoldByUserId = userId, WorkSessionId = sessionId,
                Payments = [], Credit = new PosSaleCreditContract(customer, 11900,
                    new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.FromHours(-5)), PartySiteId: site) });
            sale = sale with { UblSnapshot = sale.UblSnapshot! with { PaymentFormCode = "2", DueDate = new(2026, 8, 31) } };
            using (var upload = fixture.CreateUploadMessage(sale))
            using (var accepted = await fixture.CreateClient().SendAsync(upload))
                Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
            var debt = await ScalarAsync<Guid>("SELECT ReceivableId FROM dbo.Receivables WHERE SourceDocumentId=@Id", sale.DocumentId);
            var sourceHash = await ScalarAsync<string>("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',PayloadJson),2) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesInvoice'", sale.DocumentId);
            if (priorPayment > 0)
            {
                await using var connection = new SqlConnection(fixture.ConnectionString);
                await connection.OpenAsync();
                await fixture.EnsurePortfolioPaymentSeriesAsync(connection, "ReceivablePayment", "RCC");
                var prior = new ConfirmCustomerPaymentRequest(Guid.NewGuid(), fixture.BusinessId, customer, null,
                    new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero), "COP", "Abono anterior independiente",
                    [new(debt, priorPayment)], [new("Cash", priorPayment)]);
                await scope.ServiceProvider.GetRequiredService<ReceivablesService>().ConfirmPaymentAsync(
                    new(fixture.UserId, fixture.TenantId, fixture.BusinessId, new HashSet<string> { ReceivablesPermissionCodes.RegisterPayment }),
                    Guid.NewGuid().ToString("N"), prior);
            }
            var remaining = 11900 - priorPayment;
            var group = method is "DebitCard" or "CreditCard" ? "Card" : method;
            var closure = await sessions.CloseFromDeviceAsync(identity, sessionId, Guid.NewGuid().ToString("N"),
                new CloseWorkSessionRequest(group == "Cash" ? remaining : 0, null, fixture.UserId,
                    [new("Cash", group == "Cash" ? remaining : 0), new("Card", group == "Card" ? remaining : 0), new("Transfer", group == "Transfer" ? remaining : 0)]));
            using var reviewer = fixture.CreateUserClient(userId, WorkSessionPermissionCodes.ReadCashDifferences,
                WorkSessionPermissionCodes.ReconcileClosures, CommercePermissionCodes.SalesCorrectPaymentMethods);
            var path = $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId}";
            var page = (await reviewer.GetFromJsonAsync<WorkSessionPaymentVerificationPage>(path + "/payment-verifications/page?page=1&pageSize=100"))!;
            var item = Assert.Single(page.Items, row => row.SourceId == sale.DocumentId);
            Assert.Equal("CreditSale", item.MovementType);
            Assert.Equal(remaining, item.CollectibleAmount);
            var franchise = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code;
            var correction = new WorkSessionPaymentCorrection(item.VerificationKey, group, remaining, "Medio real del recaudo",
                method, group == "Card" ? franchise : null, group == "Card" ? "APROBACION-CREDITO" : null,
                method == "Transfer" ? "REF-CREDITO" : null, form.PaymentMethodId, form.Version);
            foreach (var invalid in new[] { correction with { Amount = remaining + 1 }, correction with { Amount = remaining - 1 },
                correction with { PaymentMethodId = null, PaymentMethodVersion = null } })
            {
                using var rejected = await reviewer.PostAsJsonAsync(path + "/payment-corrections/validate", new[] { invalid });
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            using (var preview = await reviewer.PostAsJsonAsync(path + "/payment-corrections/validate", new[] { correction }))
                Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
            var request = new ReconcileWorkSessionClosureRequest(closure.PaymentTotals.Where(row => row.RequiresCount)
                .Select(row => new ReconcileWorkSessionClosureLine(row.PaymentMethodCode, row.PaymentMethodCode == group ? remaining : 0, true, null)).ToArray(),
                [], "Crédito corregido al recaudo real", [new(item.VerificationKey, "Verified")], [correction]);
            var key = Guid.NewGuid().ToString("N");
            var store = scope.ServiceProvider.GetRequiredService<IWorkSessionStore>();
            var acceptedCorrection = await store.ReconcileClosureAsync(identity, closure.WorkSessionClosureId, key, request, default);
            // Accepted/reserved is not applied: the invoice keeps its original
            // credit label until the canonical financial transaction completes.
            await AssertCreditHistoryAsync(sale, "Credit", 11900, null);
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT available.AvailableAmount FROM dbo.Receivables debt CROSS APPLY accounting.ObligationAvailability(debt.BusinessId,N'Receivable',debt.ReceivableId,debt.OutstandingAmount,NULL) available WHERE debt.ReceivableId=@Id", debt));
            Assert.Equal(remaining, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", debt));
            var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
            if (accounting)
                await processor.ProcessAsync(closure.WorkSessionClosureId, WorkSessionAccountingDocumentTypes.CashDifference, fixture.BusinessId, default);
            for (var retry = 0; retry < 2; retry++)
            {
                await processor.ProcessAsync(acceptedCorrection.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);
                Assert.True((await store.ReconcileClosureAsync(identity, closure.WorkSessionClosureId, key, request, default)).IdempotentReplay);
            }
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", debt));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceivableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Adjustment'", acceptedCorrection.ReconciliationId));
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT available.PendingAdjustmentAmount FROM dbo.Receivables debt CROSS APPLY accounting.ObligationAvailability(debt.BusinessId,N'Receivable',debt.ReceivableId,debt.OutstandingAmount,NULL) available WHERE debt.ReceivableId=@Id", debt));
            Assert.Equal(sourceHash, await ScalarAsync<string>("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',PayloadJson),2) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesInvoice'", sale.DocumentId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@Id", sale.DocumentId));
            Assert.Equal(accounting ? 1 : 0, await CountAsync("AccountingEntries", "SourceDocumentId", acceptedCorrection.ReconciliationId));
            if (accounting) await AssertBalancedAsync(acceptedCorrection.ReconciliationId);
            var reporting = scope.ServiceProvider.GetRequiredService<Auraly.Infrastructure.Persistence.SqlSalesReportingProcessor>();
            await reporting.ProcessAsync(sale.DocumentId, "SalesInvoice", fixture.BusinessId, 1, default);
            for (var replay = 0; replay < 2; replay++)
                await reporting.ProcessAsync(acceptedCorrection.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, 1, default);
            Assert.Equal(remaining, await ScalarAsync<decimal>("SELECT CollectedAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
            Assert.Equal(priorPayment, await ScalarAsync<decimal>("SELECT CreditAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale.DocumentId));
            Assert.Equal(method, await ScalarAsync<string>("SELECT MethodCode FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'Payment'", sale.DocumentId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", acceptedCorrection.ReconciliationId));
            await AssertCreditHistoryAsync(sale, method, remaining, form.Name, priorPayment);
        }
        finally { await RestoreOpeningTestSettingsAsync(settings); }
    }

    private async Task AssertCreditHistoryAsync(PosSaleUploadRequest sale, string method,
        decimal amount, string? formName, decimal residualCredit = 0, int tenderCount = 1)
    {
        using var client = fixture.CreateAdminClient("sales.reprint");
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/pos/drafts/sales/search",
            new SearchOnlineSalesIssuedSalesRequest(new(fixture.BusinessId), sale.DocumentNumber.FullNumber, Take: 10, DateRangeVersion: 2));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var row = Assert.Single((await response.Content.ReadFromJsonAsync<OnlineSalesIssuedSalePage>())!.Items);
        var payments = row.Payments!.Where(payment => payment.MethodCode == method).ToArray();
        Assert.Equal(tenderCount,payments.Length);
        Assert.Equal(amount, payments.Sum(payment=>payment.Amount));
        if (formName is not null) Assert.All(payments,payment=>Assert.Equal(formName,payment.FormName));
        if (residualCredit > 0)
            Assert.Equal(residualCredit, Assert.Single(row.Payments!, payment => payment.MethodCode == "Credit").Amount);
        Assert.Equal(tenderCount+(residualCredit>0?1:0),row.Payments!.Count);
    }
}
