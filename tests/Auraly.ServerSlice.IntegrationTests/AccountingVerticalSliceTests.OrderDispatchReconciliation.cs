using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Application.Dispatching;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;
using Auraly.Infrastructure.Dispatching;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData("Cash", "Transfer", true)]
    [InlineData("DebitCard", "CreditCard", false)]
    [InlineData("CreditCard", "Cash", false)]
    [InlineData("Transfer", "DebitCard", false)]
    public async Task Order_invoiced_as_home_delivery_credit_changes_money_and_reporting_only_after_dispatch_reconciliation(
        string driverMethod, string finalMethod, bool useQueue)
    {
        using var admin = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            DispatchPermissionCodes.Create, DispatchPermissionCodes.Read, DispatchPermissionCodes.Prepare,
            DispatchPermissionCodes.Verify, DispatchPermissionCodes.Release, DispatchPermissionCodes.Settle,
            CommercePermissionCodes.SalesCorrectPaymentMethods);
        using (var defaults = await admin.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var active = await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate", new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) active.EnsureSuccessStatusCode();
        var creditAccount = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, admin,
            "1397", "Asset", "Receivable", "Debit");
        var types = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var definition = new SavePaymentMethodRequest(Guid.NewGuid(), $"DOM-{Guid.NewGuid():N}", "Cartera domicilio",
            types.Single(item => item.Code == "Credit").Id, creditAccount.AccountId, true, false, false, false, true, 10);
        using var saved = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{definition.PaymentMethodId}", definition);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var creditForm = (await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        var initialForm = await CreateClosureFormAsync(admin, driverMethod);
        var finalForm = await CreateClosureFormAsync(admin, finalMethod);
        var franchise = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code;
        var (customer, site) = await CreateCustomerAsync();
        var cashierId = Guid.NewGuid(); var billingSession = Guid.NewGuid();
        var firstOrder = Guid.NewGuid(); var secondOrder = Guid.NewGuid();
        await fixture.SeedConfirmedOrdersForInvoiceAsync(cashierId, billingSession, firstOrder, secondOrder);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var bind = new SqlCommand("UPDATE dbo.Orders SET CustomerId=@Customer,PartySiteId=@Site WHERE OrderId IN(@First,@Second) AND BusinessId=@Business;", connection);
            bind.Parameters.AddWithValue("@Customer", customer); bind.Parameters.AddWithValue("@Site", site);
            bind.Parameters.AddWithValue("@First", firstOrder); bind.Parameters.AddWithValue("@Second", secondOrder);
            bind.Parameters.AddWithValue("@Business", fixture.BusinessId); Assert.Equal(2, await bind.ExecuteNonQueryAsync());
            await fixture.EnsurePortfolioPaymentSeriesAsync(connection, "ReceivablePayment", "RCC");
        }
        using var cashier = fixture.CreateUserClient(cashierId, CommercePermissionCodes.SalesCreate,
            OrderPermissionCodes.Read, OrderPermissionCodes.Recover, OrderPermissionCodes.Invoice);
        var invoiceCommand = new InvoiceOrdersRequest(billingSession, fixture.WarehouseId, cashierId, [firstOrder, secondOrder],
            "Credit", null, DocumentType: PosSaleDocumentTypes.Receipt, PaymentMethodId: creditForm.PaymentMethodId,
            PaymentMethodVersion: creditForm.Version, PaymentFormCode: creditForm.Code, PaymentFormName: creditForm.Name, AccountId: creditForm.AccountId);
        var invoiceKey = Guid.NewGuid().ToString("N");
        using var invoiceMessage = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/orders/invoice") { Content = JsonContent.Create(invoiceCommand) };
        invoiceMessage.Headers.Add("Idempotency-Key", invoiceKey);
        using var invoiced = await cashier.SendAsync(invoiceMessage);
        Assert.True(invoiced.IsSuccessStatusCode, await invoiced.Content.ReadAsStringAsync());
        var batch = (await invoiced.Content.ReadFromJsonAsync<InvoiceOrdersResponse>())!;
        Assert.Equal(2, batch.CompletedCount); Assert.Equal(0, batch.FailedCount);
        var sale = Assert.Single(batch.Results, item => item.OrderId == firstOrder).DocumentId!.Value;
        var otherSale = Assert.Single(batch.Results, item => item.OrderId == secondOrder).DocumentId!.Value;
        await AssertBalancedAsync(sale); await AssertBalancedAsync(otherSale);
        Assert.Equal(10000m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id", sale));
        Assert.Equal(creditForm.PaymentMethodId, await ScalarAsync<Guid>("SELECT CreditPaymentMethodId FROM dbo.SalesDocuments WHERE DocumentId=@Id", sale));
        using var scope = fixture.CreateScope();
        var reporting = scope.ServiceProvider.GetRequiredService<Auraly.Infrastructure.Persistence.SqlSalesReportingProcessor>();
        await reporting.ProcessAsync(sale, "SalesReceipt", fixture.BusinessId, 1, default);
        var before = await BeforeApprovalAsync();
        var originalSession = await ScalarAsync<string>("SELECT (SELECT * FROM dbo.WorkSessionMovements WHERE WorkSessionId=@Id ORDER BY WorkSessionMovementId FOR JSON PATH,INCLUDE_NULL_VALUES)", billingSession);
        var driverId = Guid.NewGuid(); var carrier = await fixture.SeedDispatchCarrierAsync(driverId);
        var dispatch = await PostAsync<DispatchMutationResult>("/api/commerce/v1/dispatches",
            new CreateDispatchRequest(fixture.BusinessId, new(2026, 11, 26), "Transportador asignado", "QA-123", carrier, null, [sale]));
        foreach (var transition in new[] { "prepare", "start-verification" })
            dispatch = await PostAsync<DispatchMutationResult>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/{transition}", new DispatchTransitionRequest(dispatch.RowVersion, Guid.NewGuid().ToString("N")));
        var detail = (await admin.GetFromJsonAsync<DispatchDetail>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}"))!;
        var line = Assert.Single(detail.Lines);
        dispatch = await PostAsync<DispatchMutationResult>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/verification-events",
            new DispatchVerificationRequest(line.DispatchLineId, line.AssignedQuantity, null, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
        foreach (var transition in new[] { "complete-verification", "release" })
            dispatch = await PostAsync<DispatchMutationResult>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/{transition}", new DispatchTransitionRequest(dispatch.RowVersion, Guid.NewGuid().ToString("N")));
        using var driver = fixture.CreateUserClient(driverId, DispatchPermissionCodes.ExecuteDeliveries);
        var execution = (await driver.GetFromJsonAsync<DispatchExecutionDetail>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/execution"))!;
        Assert.True(execution.IsAssignedDriver);
        var creatorExecution = (await admin.GetFromJsonAsync<DispatchExecutionDetail>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/execution"))!;
        Assert.False(creatorExecution.IsAssignedDriver);
        var source = Assert.Single(execution.Documents);
        var card = driverMethod is "DebitCard" or "CreditCard";
        var deliveryCommand = new RecordDispatchDeliveryRequest(source.DispatchSourceDocumentId, "Delivered", null, null, null, null,
            DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), [new("CreditAdvance", driverMethod, 10000,
                driverMethod == "Transfer" ? "TRF-DRIVER" : null, null, initialForm.PaymentMethodId, initialForm.Version,
                card ? franchise : null, card ? "APR-DRIVER" : null),
                new("CreditDocument", null, 0, null, "https://qa.invalid/factura-firmada.png")], []);
        using (var delivered = await driver.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/delivery-results", deliveryCommand))
        { Assert.True(delivered.IsSuccessStatusCode, await delivered.Content.ReadAsStringAsync()); execution = (await delivered.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!; }
        Assert.Equal(before, await BeforeApprovalAsync());
        using (var deniedClose = await admin.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/close-route",
            new CloseDispatchRouteRequest(0, null, Guid.NewGuid().ToString("N"), 0, 0)))
            Assert.Equal(HttpStatusCode.Forbidden, deniedClose.StatusCode);
        using (var closed = await driver.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/close-route",
            new CloseDispatchRouteRequest(driverMethod == "Cash" ? 10000 : 0, null, Guid.NewGuid().ToString("N"), card ? 10000 : 0, driverMethod == "Transfer" ? 10000 : 0)))
        { Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync()); execution = (await closed.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!; }
        Assert.Equal("PendingReview", execution.Settlement!.Status);
        Assert.Equal(before, await BeforeApprovalAsync());
        var payment = Assert.Single(Assert.Single(execution.Documents).Payments, item => item.ApplicationType == "CreditAdvance");
        var finalCard = finalMethod is "DebitCard" or "CreditCard";
        var change = new CorrectDispatchPaymentRequest(finalForm.PaymentMethodId, finalForm.Version, finalMethod,
            finalMethod == "Transfer" ? "TRF-ADMIN" : null, finalCard ? franchise : null, finalCard ? "APR-ADMIN" : null, "Medio real verificado por administración");
        using (var denied = await driver.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/payments/{payment.PaymentId}/correction", change)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var corrected = await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/payments/{payment.PaymentId}/correction", change))
        { Assert.True(corrected.IsSuccessStatusCode, await corrected.Content.ReadAsStringAsync()); payment = (await corrected.Content.ReadFromJsonAsync<DispatchPaymentReviewResult>())!.Payment; }
        Assert.Equal(before, await BeforeApprovalAsync());
        if (finalMethod != "Cash")
        {
            using var verified = await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/payments/{payment.PaymentId}/verification", new VerifyDispatchPaymentRequest(true, payment.CorrectedAt));
            Assert.True(verified.IsSuccessStatusCode, await verified.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, await BeforeApprovalAsync());
        var approval = new SettleDispatchRequest(finalMethod == "Cash" ? 10000 : 0, null, Guid.NewGuid().ToString("N"));
        DispatchSettlementHostedService? worker = null;
        if (useQueue)
            await CompleteThroughQueueAsync();
        else
        {
            await PostAsync<DispatchExecutionDetail>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/settle", approval);
            worker = ActivatorUtilities.CreateInstance<DispatchSettlementHostedService>(fixture.Services);
            await worker.StartAsync(default);
        }
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id", dispatch.DispatchId) != "Closed" && DateTimeOffset.UtcNow < deadline) await Task.Delay(100);
            Assert.Equal("Closed", await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id", dispatch.DispatchId));
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id", sale));
            Assert.Equal(20000m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id", otherSale));
            var settlement = await ScalarAsync<Guid>("SELECT DispatchSettlementId FROM dispatch.Closures WHERE DispatchId=@Id", dispatch.DispatchId);
            Assert.Equal("CommercialEffectsApplied", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", settlement));
            var customerPayment = await ScalarAsync<Guid>("SELECT application.PaymentId FROM dbo.CustomerPaymentApplications application JOIN dbo.Receivables debt ON debt.ReceivableId=application.ReceivableId WHERE debt.SourceDocumentId=@Id", sale);
            Assert.Equal("Posted", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", customerPayment));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Id", settlement));
            await reporting.ProcessAsync(settlement, DispatchAccountingDocumentTypes.CashDifference, fixture.BusinessId, 1, default);
            await PostAsync<DispatchExecutionDetail>($"/api/commerce/v1/dispatches/{dispatch.DispatchId}/settle", approval);
            await reporting.ProcessAsync(settlement, DispatchAccountingDocumentTypes.CashDifference, fixture.BusinessId, 1, default);
            Assert.Equal(10000m, await ScalarAsync<decimal>("SELECT CollectedAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale));
            Assert.Equal(0m, await ScalarAsync<decimal>("SELECT CreditAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id", sale));
            Assert.Equal(finalMethod, await ScalarAsync<string>("SELECT MethodCode FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'Payment'", sale));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPaymentApplications application JOIN dbo.Receivables debt ON debt.ReceivableId=application.ReceivableId WHERE debt.SourceDocumentId=@Id", sale));
            Assert.Equal(10000m, await ScalarAsync<decimal>("SELECT COALESCE(SUM(Debit-Credit),0) FROM dbo.AccountingEntryLines WHERE AccountId=@Id", finalForm.AccountId));
            Assert.Equal(20000m, await ScalarAsync<decimal>("SELECT COALESCE(SUM(Debit-Credit),0) FROM dbo.AccountingEntryLines WHERE AccountId=@Id", creditAccount.AccountId));
            Assert.Equal(originalSession, await ScalarAsync<string>("SELECT (SELECT * FROM dbo.WorkSessionMovements WHERE WorkSessionId=@Id ORDER BY WorkSessionMovementId FOR JSON PATH,INCLUDE_NULL_VALUES)", billingSession));
            // The native receivable payment owns the journal. With no cash
            // difference the dispatch completion must not create another one.
            await AssertBalancedAsync(customerPayment);
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", settlement));
        }
        finally
        {
            if (worker is not null) { await worker.StopAsync(default); worker.Dispose(); }
        }

        async Task CompleteThroughQueueAsync()
        {
            var provider = scope.ServiceProvider;
            var deliveries = new DispatchDeliveryService(new SqlDispatchDeliveryStore(
                provider.GetRequiredService<DispatchingSqlConnectionFactory>(), queueSettlement: true));
            var actor = new DispatchActorIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { DispatchPermissionCodes.Settle, DispatchPermissionCodes.ReadAll }, true);
            var accepted = await deliveries.SettleAsync(actor, dispatch.DispatchId, approval, default);
            Assert.Equal("Processing", accepted.Settlement!.Status);
            Assert.Equal(before, await BeforeApprovalAsync());

            var messages = new CapturedDispatchSignals();
            var activation = new SqlDispatchSettlementActivation(provider.GetRequiredService<SqlServerConnectionFactory>(),
                messages, NullLogger<SqlDispatchSettlementActivation>.Instance);
            var preparation = provider.GetRequiredService<DispatchSettlementPreparation>();

            await activation.RecoverAsync(default);
            var initial = Assert.Single(messages.Items);
            await preparation.ProcessAsync(initial, default);
            await preparation.ProcessAsync(initial, default);
            Assert.Equal(before, await BeforeApprovalAsync());
            await activation.RecoverAsync(default);
            Assert.Equal(2, messages.Items.Count);

            var finance = provider.GetRequiredService<TestAccountingProcessingSignalPublisher>();
            finance.Pause();
            try
            {
                await preparation.ProcessAsync(messages.Items[1], default);
                Assert.Equal(before, await BeforeApprovalAsync());
                Assert.Equal("Accepted", await ScalarAsync<string>("""
                    SELECT payment.Status FROM dbo.CustomerPayments payment
                    JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=payment.PaymentId
                    JOIN dbo.Receivables debt ON debt.ReceivableId=application.ReceivableId
                    WHERE debt.SourceDocumentId=@Id;
                    """, sale));
                await activation.RecoverAsync(default);
                Assert.Equal(2, messages.Items.Count);
            }
            finally { finance.Resume(); }
            var child = Assert.Single(finance.Drain());
            await finance.PublishAsync(child);
            await finance.PublishAsync(child);
            await activation.RecoverAsync(default);
            Assert.Equal(3, messages.Items.Count);
            await preparation.ProcessAsync(messages.Items[2], default);
            await preparation.ProcessAsync(messages.Items[2], default);
        }

        async Task<T> PostAsync<T>(string path, object body)
        {
            using var response = await admin.PostAsJsonAsync(path, body);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        Task<string> BeforeApprovalAsync() => ScalarAsync<string>("""
            SELECT (SELECT
              (SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id) Debt,
              (SELECT COUNT(*) FROM dbo.CustomerPaymentApplications a JOIN dbo.Receivables d ON d.ReceivableId=a.ReceivableId
                JOIN dbo.CustomerPayments payment ON payment.PaymentId=a.PaymentId AND payment.Status=N'Processed'
                WHERE d.SourceDocumentId=@Id) Applications,
              (SELECT COUNT(*) FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId WHERE l.AccountId=(SELECT CreditAccountId FROM dbo.SalesDocuments WHERE DocumentId=@Id)) Entries,
              JSON_QUERY((SELECT * FROM reporting.SalesReportDocuments WHERE DocumentId=@Id FOR JSON PATH,INCLUDE_NULL_VALUES)) Report,
              JSON_QUERY((SELECT * FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id ORDER BY PaymentNumber FOR JSON PATH,INCLUDE_NULL_VALUES)) Payments
              FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """, sale);
    }

    private sealed class CapturedDispatchSignals : IDispatchSettlementSignalPublisher
    {
        public List<DispatchSettlementSignal> Items { get; } = [];

        public Task PublishAsync(DispatchSettlementSignal signal, CancellationToken cancellationToken)
        {
            Items.Add(signal);
            return Task.CompletedTask;
        }
    }
}
