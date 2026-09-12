using System.Data;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Sales;
using Auraly.Contracts.Returns;
using Auraly.Contracts.WorkSessions;
using Auraly.Commerce.Accounting.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Accounting")]
public sealed class ReceivablesVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Commercial_receipt_credit_and_collection_work_without_accounting_and_are_not_posted_retroactively()
    {
        var (customerId, userId) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open,
            ReceivablesPermissionCodes.Read,
            ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.RegisterPayment,
            SalesReturnPermissionCodes.Read,
            SalesReturnPermissionCodes.Create,
            SalesReturnPermissionCodes.Confirm);
        client.Timeout = TimeSpan.FromSeconds(60);
        using (var profile = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/customers/{customerId:D}/credit",
                   new UpdateCustomerCreditProfileRequest(
                       fixture.BusinessId, 500_000m, 30, true)))
            profile.EnsureSuccessStatusCode();

        var originalAccounting = await DisableAccountingAsync();
        var accountingRestored = false;
        try
        {
            var workSession = await fixture.OpenWorkSessionAsync(client);
            var cashDraft = await CaptureAsync(client,
                await OpenDraftAsync(client, workSession.WorkSessionId));
            var cashSale = await CompleteAsync(client, cashDraft,
                new CompleteOnlineSalesDraftRequest(
                    cashDraft.Version,
                    [new OnlineSalesPayment("Cash", cashDraft.PayableAmount, null)],
                    DocumentType: PosSaleDocumentTypes.Receipt),
                $"commercial-cash-control-{Guid.NewGuid():N}");
            await AssertCommercialOnlyProcessingAsync(cashSale.Receipt.DocumentId);
            Assert.Equal(1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id AND MovementType=N'SalePayment'",
                cashSale.Receipt.DocumentId));
            Assert.Equal("Completed", await ScalarAsync<string>(
                "SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id",
                cashSale.Receipt.DocumentId));

            var draft = await CaptureAsync(client,
                await OpenDraftAsync(client, workSession.WorkSessionId));
            var selection = await SelectCustomerAsync(client, draft, customerId);
            var sale = await CompleteAsync(client, selection.Draft,
                new CompleteOnlineSalesDraftRequest(
                    selection.Draft.Version, [],
                    new OnlineSalesCreditTerms(selection.Draft.PayableAmount),
                    DocumentType: PosSaleDocumentTypes.Receipt),
                $"commercial-credit-{Guid.NewGuid():N}");

            Assert.Equal(PosSaleDocumentTypes.Receipt, sale.Receipt.DocumentType);
            var receivable = await ReadReceivableAsync(sale.Receipt.DocumentId);
            Assert.Equal(sale.Receipt.PayableAmount, receivable.OutstandingAmount);
            Assert.Equal(PosSaleDocumentTypes.Receipt, await ScalarAsync<string>(
                "SELECT SourceDocumentType FROM dbo.Receivables WHERE SourceDocumentId=@Id",
                sale.Receipt.DocumentId));
            await AssertCommercialOnlyProcessingAsync(sale.Receipt.DocumentId);

            var partialAmount = decimal.Round(receivable.OriginalAmount * .4m, 4);
            var payment = new ConfirmCustomerPaymentRequest(
                Guid.NewGuid(), fixture.BusinessId, customerId,
                workSession.WorkSessionId, DateTimeOffset.UtcNow, "COP",
                CustomerPaymentMethods.Cash, null, "Abono sin contabilidad",
                [new CustomerPaymentAllocationRequest(
                    receivable.ReceivableId, partialAmount)]);
            var paymentKey = $"commercial-payment-{payment.PaymentId:N}";
            using (var response = await SendAsync(client,
                       "/api/commerce/v1/receivable-payments/confirm", payment, paymentKey))
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            Assert.Equal(receivable.OriginalAmount - partialAmount,
                await ScalarAsync<decimal>(
                    "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
                    receivable.ReceivableId));
            Assert.Equal("Processed", await ScalarAsync<string>(
                "SELECT Status FROM dbo.CustomerPayments WHERE PaymentId=@Id",
                payment.PaymentId));
            await AssertCommercialOnlyProcessingAsync(payment.PaymentId);

            using (var replay = await SendAsync(client,
                       "/api/commerce/v1/receivable-payments/confirm", payment, paymentKey))
            {
                Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
                Assert.True((await replay.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>())!
                    .IdempotentReplay);
            }
            Assert.Equal(1, await CountAsync(
                "ReceivableTransactions", "SourceDocumentId", payment.PaymentId));

            var returnRequest = new ConfirmSalesReturnRequest(
                Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                sale.Receipt.DocumentId, DateTimeOffset.UtcNow,
                ReturnEconomicResolutions.CustomerCredit, null,
                "Devolucion comercial sin contabilidad",
                [new ConfirmSalesReturnLineRequest(
                    1, .2m, ReturnInventoryDispositions.Sellable)],
                null, null, "CustomerChangedMind");
            using (var response = await SendAsync(client,
                       "/api/commerce/v1/sales-returns/confirm", returnRequest,
                       $"commercial-return-{returnRequest.ReturnId:N}"))
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var returnApplication = await ScalarAsync<decimal>(
                "SELECT Amount FROM dbo.SalesReturnReceivableApplications WHERE ReturnId=@Id",
                returnRequest.ReturnId);
            Assert.Equal(receivable.OriginalAmount - partialAmount - returnApplication,
                await ScalarAsync<decimal>(
                    "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
                    receivable.ReceivableId));
            await AssertCommercialOnlyProcessingAsync(returnRequest.ReturnId);

            await RestoreAccountingAsync(originalAccounting);
            accountingRestored = true;
            await ReprocessAsync(sale.Receipt.DocumentId, PosSaleDocumentTypes.Receipt);
            await ReprocessAsync(payment.PaymentId, ReceivablesDocumentTypes.Payment);
            await ReprocessAsync(returnRequest.ReturnId, SalesReturnDocumentTypes.SalesReturn);
            await AssertCommercialOnlyProcessingAsync(sale.Receipt.DocumentId);
            await AssertCommercialOnlyProcessingAsync(payment.PaymentId);
            await AssertCommercialOnlyProcessingAsync(returnRequest.ReturnId);

            var accountedDraft = await CaptureAsync(client,
                await OpenDraftAsync(client, workSession.WorkSessionId));
            var accountedSelection = await SelectCustomerAsync(
                client, accountedDraft, customerId);
            var accountedSale = await CompleteAsync(client, accountedSelection.Draft,
                new CompleteOnlineSalesDraftRequest(
                    accountedSelection.Draft.Version, [],
                    new OnlineSalesCreditTerms(accountedSelection.Draft.PayableAmount),
                    DocumentType: PosSaleDocumentTypes.Receipt),
                $"accounted-commercial-credit-{Guid.NewGuid():N}");
            Assert.Equal("Posted", await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                accountedSale.Receipt.DocumentId));
            Assert.True(await ScalarAsync<bool>(
                "SELECT AccountingEntryRequired FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                accountedSale.Receipt.DocumentId));
            Assert.Equal(1, await CountAsync(
                "AccountingEntries", "SourceDocumentId", accountedSale.Receipt.DocumentId));
            Assert.Equal(PosSaleDocumentTypes.Receipt, await ScalarAsync<string>(
                "SELECT SourceDocumentType FROM dbo.Receivables WHERE SourceDocumentId=@Id",
                accountedSale.Receipt.DocumentId));
        }
        finally
        {
            if (!accountingRestored) await RestoreAccountingAsync(originalAccounting);
        }
    }

    [Fact]
    public async Task Credit_sale_is_rejected_when_customer_credit_is_not_enabled()
    {
        var (customerId, userId) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open);
        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await CaptureAsync(client, await OpenDraftAsync(client, workSession.WorkSessionId));
        var selection = await SelectCustomerAsync(client, draft, customerId);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/complete")
        {
            Content = JsonContent.Create(new CompleteOnlineSalesDraftRequest(
                selection.Draft.Version, [],
                new OnlineSalesCreditTerms(selection.Draft.PayableAmount)))
        };
        request.Headers.Add("Idempotency-Key", $"disabled-credit-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no tiene habilitada la venta a crédito",
            await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesDocuments WHERE CustomerId=@Id", customerId));
    }

    [Fact]
    public async Task Credit_due_date_is_derived_from_the_server_customer_terms()
    {
        var (customerId, userId) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open,
            ReceivablesPermissionCodes.ManageCredit);
        using (var profile = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/customers/{customerId:D}/credit",
                   new UpdateCustomerCreditProfileRequest(
                       fixture.BusinessId, 500_000m, 30, true)))
            profile.EnsureSuccessStatusCode();

        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await CaptureAsync(client,
            await OpenDraftAsync(client, workSession.WorkSessionId));
        var selection = await SelectCustomerAsync(client, draft, customerId);
        var startedAt = DateTimeOffset.UtcNow;
        var sale = await CompleteAsync(client, selection.Draft,
            new CompleteOnlineSalesDraftRequest(
                selection.Draft.Version,
                [],
                new OnlineSalesCreditTerms(selection.Draft.PayableAmount),
                DocumentType: PosSaleDocumentTypes.Receipt),
            $"credit-due-date-{Guid.NewGuid():N}");

        var receivable = await ReadReceivableAsync(sale.Receipt.DocumentId);
        Assert.Equal(customerId, receivable.CustomerId);
        Assert.InRange(
            receivable.DueDate,
            startedAt.AddDays(30),
            DateTimeOffset.UtcNow.AddDays(30));
    }

    [Fact]
    public async Task Enrolled_device_credit_validation_uses_the_current_server_balance()
    {
        var (customerId, userId) = await ConfigureAsync();
        using (var user = fixture.CreateUserClient(
                   userId,
                   ReceivablesPermissionCodes.ManageCredit))
        using (var profile = await user.PutAsJsonAsync(
                   $"/api/commerce/v1/customers/{customerId:D}/credit",
                   new UpdateCustomerCreditProfileRequest(
                       fixture.BusinessId, 500_000m, 30, true)))
            profile.EnsureSuccessStatusCode();

        using var device = fixture.CreateClient();
        device.DefaultRequestHeaders.Add(
            "X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        device.DefaultRequestHeaders.Add(
            "X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        var startedAt = DateTimeOffset.UtcNow;
        using (var allowed = await device.PostAsJsonAsync(
                   "/api/pos/v1/sales/credit-validation",
                   new PosCreditValidationRequest(
                       fixture.BusinessId, customerId, 100_000m,
                       FiscalEnvironment: 2)))
        {
            allowed.EnsureSuccessStatusCode();
            var result = await allowed.Content.ReadFromJsonAsync<PosCreditValidationResult>();
            Assert.NotNull(result);
            Assert.True(result.IsAllowed);
            Assert.Equal(500_000m, result.AvailableCredit);
            Assert.NotNull(result.FiscalMaterial);
            Assert.Equal(
                ServerSliceFixture.SupplierTaxId,
                result.FiscalMaterial.Supplier.Identification);
            Assert.Equal(customerId, result.CustomerId);
            Assert.NotNull(result.DueDate);
            Assert.InRange(
                result.DueDate.Value,
                startedAt.AddDays(30),
                DateTimeOffset.UtcNow.AddDays(30));
        }

        using var rejected = await device.PostAsJsonAsync(
            "/api/pos/v1/sales/credit-validation",
            new PosCreditValidationRequest(
                fixture.BusinessId, customerId, 600_000m));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains(
            "supera el cupo disponible",
            await rejected.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Credit_sale_and_customer_payment_are_scoped_idempotent_and_accounted_once()
    {
        var (customerId, userId) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open,
            ReceivablesPermissionCodes.Read,
            ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.RegisterPayment);
        client.Timeout = TimeSpan.FromSeconds(60);

        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/customers/{customerId:D}/credit",
                   new UpdateCustomerCreditProfileRequest(
                       fixture.BusinessId, 500_000m, 30, true)))
        {
            response.EnsureSuccessStatusCode();
            var profile = await response.Content.ReadFromJsonAsync<CustomerCreditProfile>();
            Assert.NotNull(profile);
            Assert.Equal(500_000m, profile.AvailableCredit);
        }

        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await OpenDraftAsync(client, workSession.WorkSessionId);
        draft = await CaptureAsync(client, draft);
        var selection = await SelectCustomerAsync(client, draft, customerId);
        Assert.NotNull(selection.Customer);
        Assert.True(selection.Customer.IsCreditEnabled);
        Assert.Equal(500_000m, selection.Customer.AvailableCredit);
        var checkoutKey = $"receivable-sale-{Guid.NewGuid():N}";
        var checkout = await CompleteAsync(client, selection.Draft,
            new CompleteOnlineSalesDraftRequest(
                selection.Draft.Version,
                [],
                new OnlineSalesCreditTerms(selection.Draft.PayableAmount)),
            checkoutKey);

        var receivable = await ReadReceivableAsync(checkout.Receipt.DocumentId);
        Assert.Equal(customerId, receivable.CustomerId);
        Assert.Equal(checkout.Receipt.PayableAmount, receivable.OriginalAmount);
        Assert.Equal(checkout.Receipt.PayableAmount, receivable.OutstandingAmount);
        Assert.Equal("Open", receivable.Status);
        Assert.Equal(0, await CountAsync(
            "SalesPayments", "DocumentId", checkout.Receipt.DocumentId));
        Assert.Equal(1, await CountAsync(
            "ReceivableTransactions", "SourceDocumentId", checkout.Receipt.DocumentId));
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", checkout.Receipt.DocumentId));
        Assert.Equal(checkout.Receipt.PayableAmount,
            await AccountAmountAsync(checkout.Receipt.DocumentId, "130505", true));

        using (var response = await client.GetAsync(
                   $"/api/commerce/v1/receivables/{receivable.ReceivableId:D}"))
        {
            response.EnsureSuccessStatusCode();
            var detail = await response.Content.ReadFromJsonAsync<ReceivableDetail>();
            Assert.NotNull(detail);
            Assert.Equal(checkout.Receipt.DocumentNumber, detail.DocumentNumber);
            Assert.Equal("Opening", Assert.Single(detail.Transactions).Type);
        }

        var partialAmount = decimal.Round(receivable.OriginalAmount * 0.4m, 4);
        var payment = new ConfirmCustomerPaymentRequest(
            Guid.NewGuid(), fixture.BusinessId, customerId,
            workSession.WorkSessionId, DateTimeOffset.UtcNow, "COP",
            CustomerPaymentMethods.Cash, null, "Abono E2E",
            [new CustomerPaymentAllocationRequest(receivable.ReceivableId, partialAmount)]);
        var paymentKey = $"receivable-payment-{payment.PaymentId:N}";
        CustomerPaymentAcceptance acceptance;
        using (var response = await SendAsync(client,
                   "/api/commerce/v1/receivable-payments/confirm", payment, paymentKey))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            acceptance = await response.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>()
                ?? throw new InvalidOperationException("Empty payment acceptance.");
            Assert.StartsWith("RCC", acceptance.DocumentNumber);
            Assert.False(acceptance.IdempotentReplay);
        }

        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.CustomerPayments WHERE PaymentId=@Id", payment.PaymentId));
        Assert.Equal(receivable.OriginalAmount - partialAmount, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivable.ReceivableId));
        Assert.Equal("PartiallyPaid", await ScalarAsync<string>(
            "SELECT Status FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivable.ReceivableId));
        Assert.Equal(1, await CountAsync(
            "CustomerPaymentApplications", "PaymentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "ReceivableTransactions", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "ServerOutboxMessages", "DocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'receivable-payment:',CONVERT(nvarchar(36),@Id))",
            payment.PaymentId));
        Assert.Equal(partialAmount,
            await AccountAmountAsync(payment.PaymentId, "110505", true));
        Assert.Equal(partialAmount,
            await AccountAmountAsync(payment.PaymentId, "130505", false));

        using (var response = await SendAsync(client,
                   "/api/commerce/v1/receivable-payments/confirm", payment, paymentKey))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var replay = await response.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>();
            Assert.NotNull(replay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(acceptance.MovementId, replay.MovementId);
        }
        Assert.Equal(1, await CountAsync(
            "ReceivableTransactions", "SourceDocumentId", payment.PaymentId));

        var remaining = receivable.OriginalAmount - partialAmount;
        var concurrentAmount = decimal.Round(remaining * 0.75m, 4);
        var first = payment with
        {
            PaymentId = Guid.NewGuid(),
            Allocations = [new(receivable.ReceivableId, concurrentAmount)]
        };
        var second = first with { PaymentId = Guid.NewGuid() };
        var responses = await Task.WhenAll(
            SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", first,
                $"concurrent-{first.PaymentId:N}"),
            SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", second,
                $"concurrent-{second.PaymentId:N}"));
        try
        {
            var outcomes = await Task.WhenAll(responses.Select(async response =>
                $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"));
            var detail = string.Join(Environment.NewLine, outcomes);
            Assert.True(responses.Count(x => x.StatusCode == HttpStatusCode.Accepted) == 1,
                detail);
            Assert.True(responses.Count(x => x.StatusCode == HttpStatusCode.Conflict) == 1,
                detail);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        Assert.Equal(remaining - concurrentAmount, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivable.ReceivableId));
    }

    [Fact]
    public async Task Customer_credit_return_is_capped_at_outstanding_and_creates_no_excess_credit()
    {
        var (customerId, userId) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Open,
            ReceivablesPermissionCodes.ManageCredit,
            SalesReturnPermissionCodes.Read,
            ReceivablesPermissionCodes.RegisterPayment,
            SalesReturnPermissionCodes.Create,
            SalesReturnPermissionCodes.Confirm);
        using (var profile = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/customers/{customerId:D}/credit",
                   new UpdateCustomerCreditProfileRequest(
                       fixture.BusinessId, 500_000m, 30, true)))
            profile.EnsureSuccessStatusCode();

        var workSession = await fixture.OpenWorkSessionAsync(client);
        var draft = await CaptureAsync(client,
            await OpenDraftAsync(client, workSession.WorkSessionId));
        var selection = await SelectCustomerAsync(client, draft, customerId);
        var checkout = await CompleteAsync(client, selection.Draft,
            new CompleteOnlineSalesDraftRequest(
                selection.Draft.Version, [],
                new OnlineSalesCreditTerms(selection.Draft.PayableAmount),
                DocumentType: PosSaleDocumentTypes.Receipt),
            $"return-credit-sale-{Guid.NewGuid():N}");
        var receivable = await ReadReceivableAsync(checkout.Receipt.DocumentId);
        using (var response = await client.GetAsync(
                   $"/api/commerce/v1/sales-returns/sales/{checkout.Receipt.DocumentId:D}"))
        {
            response.EnsureSuccessStatusCode();
            var returnable = await response.Content.ReadFromJsonAsync<ReturnableSale>()
                ?? throw new InvalidOperationException("Empty returnable sale response.");
            Assert.Equal(receivable.OutstandingAmount, returnable.ReceivableOutstanding);
        }

        var paidBeforeReturn = decimal.Round(receivable.OriginalAmount * .25m, 4);
        var payment = new ConfirmCustomerPaymentRequest(
            Guid.NewGuid(), fixture.BusinessId, customerId, workSession.WorkSessionId,
            DateTimeOffset.UtcNow, "COP", CustomerPaymentMethods.Cash, null,
            "Abono anterior a devolucion",
            [new CustomerPaymentAllocationRequest(receivable.ReceivableId, paidBeforeReturn)]);
        using (var response = await SendAsync(client,
                   "/api/commerce/v1/receivable-payments/confirm", payment,
                   $"pre-return-payment-{payment.PaymentId:N}"))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);


        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            checkout.Receipt.DocumentId, DateTimeOffset.UtcNow,
            ReturnEconomicResolutions.CustomerCredit, null,
            "Devolucion parcial aplicada a cartera",
            [new ConfirmSalesReturnLineRequest(
                1, .75m, ReturnInventoryDispositions.Sellable)],
            null, null, "CustomerChangedMind");
        using (var response = await SendAsync(client,
                   "/api/commerce/v1/sales-returns/confirm", request,
                   $"receivable-return-{request.ReturnId:N}"))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var applied = await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.SalesReturnReceivableApplications WHERE ReturnId=@Id",
            request.ReturnId);
        Assert.Equal(receivable.OriginalAmount - paidBeforeReturn, applied);
        Assert.Equal(0m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",
            receivable.ReceivableId));
        Assert.Equal("Paid", await ScalarAsync<string>(
            "SELECT Status FROM dbo.Receivables WHERE ReceivableId=@Id", receivable.ReceivableId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ReceivableTransactions WHERE TransactionType=N'Reversal' AND SourceDocumentId=@Id",
            request.ReturnId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id",
            request.ReturnId));

        var fullyPaidRequest = request with
        {
            ReturnId = Guid.NewGuid(),
            Lines = [new ConfirmSalesReturnLineRequest(
                1, .25m, ReturnInventoryDispositions.Sellable)]
        };
        using var fullyPaidResponse = await SendAsync(client,
            "/api/commerce/v1/sales-returns/confirm", fullyPaidRequest,
            $"receivable-return-paid-{fullyPaidRequest.ReturnId:N}");
        Assert.Equal(HttpStatusCode.BadRequest, fullyPaidResponse.StatusCode);
    }


    [Fact]
    public async Task Receivables_endpoints_enforce_permissions_and_business_scope()
    {
        using var denied = fixture.CreateAdminClient();
        using var list = await denied.GetAsync(
            "/api/commerce/v1/receivables?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using var scoped = fixture.CreateAdminClient(
            ReceivablesPermissionCodes.RegisterPayment);
        var payment = new ConfirmCustomerPaymentRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, "COP", CustomerPaymentMethods.Cash,
            null, null, [new(Guid.NewGuid(), 1m)]);
        using var response = await SendAsync(scoped,
            "/api/commerce/v1/receivable-payments/confirm", payment,
            $"wrong-business-{payment.PaymentId:N}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(Guid CustomerId, Guid UserId)> ConfigureAsync()
    {
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        try
        {
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.AppUsers(
                  UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
                  FirstName,LastName,IsActive,CreatedAt)
                VALUES(@UserId,@TenantId,@Username,UPPER(@Username),@Email,
                  UPPER(@Email),N'Cajero',N'Cartera',1,SYSDATETIMEOFFSET());

                INSERT dbo.Parties(
                  PartyId,TenantId,PartyType,IdentificationTypeCode,Identification,
                  NormalizedIdentification,DisplayName,LegalName,CompletionStatus,
                  IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'Organization',NULL,NULL,NULL,
                  N'Cliente crédito E2E',N'Cliente crédito E2E',N'Incomplete',1,
                  @UserId,SYSDATETIMEOFFSET());
                INSERT dbo.Customers(
                  CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries
                    WHERE BusinessId=@BusinessId AND DocumentType=N'ReceivablePayment' AND IsActive=1)
                  INSERT dbo.DocumentSeries(
                    DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                    Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,NULL,N'ReceivablePayment',N'RCC',N'00',
                    8,1,99999999,0,1,SYSDATETIMEOFFSET());
                """,
                new("@PartyId", partyId),
                new("@CustomerId", customerId),
                new("@TenantId", fixture.TenantId),
                new("@BusinessId", fixture.BusinessId),
                new("@UserId", userId),
                new("@Username", $"receivables-{userId:N}"),
                new("@Email", $"receivables-{userId:N}@test.local"));

            await EnsureAccountAsync(connection, transaction, "110505", "Caja general", "Asset", false);
            await EnsureAccountAsync(connection, transaction, "130505", "Clientes", "Asset", true);
            await EnsureAccountAsync(connection, transaction, "143505", "Inventarios", "Asset", false);
            await EnsureAccountAsync(connection, transaction, "240805", "IVA generado", "Liability", false);
            await EnsureAccountAsync(connection, transaction, "413595", "Ingresos", "Revenue", false);
            await EnsureAccountAsync(connection, transaction, "613595", "Costo de ventas", "Expense", false);
            await EnsureMappingAsync(connection, transaction, AccountingCategories.Cash, "110505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.AccountsReceivable, "130505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.Inventory, "143505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.OutputVat, "240805");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.SalesRevenue, "413595");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.CostOfGoodsSold, "613595");
            await ExecuteAsync(connection, transaction, """
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingPeriods
                    WHERE TenantId=@TenantId AND Status=N'Open'
                      AND StartsOn<='2026-08-03' AND EndsOn>='2026-08-03')
                  INSERT dbo.AccountingPeriods(
                    PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
                  VALUES(NEWID(),@TenantId,N'Periodo Receivables 2026',
                    '2026-01-01','2026-12-31',N'Open',SYSDATETIMEOFFSET());
                """, new SqlParameter("@TenantId", fixture.TenantId));
            await transaction.CommitAsync();
            return (customerId, userId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<OnlineSalesDraft> OpenDraftAsync(
        HttpClient client, Guid workSessionId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId, fixture.WarehouseId, workSessionId)));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty draft response.");
    }

    private async Task<OnlineSalesDraft> CaptureAsync(
        HttpClient client, OnlineSalesDraft draft)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items")
        {
            Content = JsonContent.Create(new AddOnlineSalesDraftItemRequest("P-E2E", 1m, draft.Version))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty capture response.");
    }

    private static async Task<OnlineSalesCustomerSelection> SelectCustomerAsync(
        HttpClient client, OnlineSalesDraft draft, Guid customerId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer")
        {
            Content = JsonContent.Create(new SelectOnlineSalesDraftCustomerRequest(
                customerId, draft.Version))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesCustomerSelection>()
            ?? throw new InvalidOperationException("Empty customer selection response.");
    }

    private static async Task<CompleteOnlineSalesDraftResponse> CompleteAsync(
        HttpClient client, OnlineSalesDraft draft,
        CompleteOnlineSalesDraftRequest command, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/complete")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>()
            ?? throw new InvalidOperationException("Empty checkout response.");
    }

    private async Task<ReceivableEvidence> ReadReceivableAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT r.ReceivableId,r.CustomerId,r.OriginalAmount,
                   r.OutstandingAmount,r.Status,r.DueDate,d.CreditAmount,
                   d.ProcessingStatus,j.Status,j.LastError
            FROM dbo.SalesDocuments d
            LEFT JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=d.DocumentId AND j.DocumentType=d.DocumentType
            LEFT JOIN dbo.Receivables r ON r.SourceDocumentId=d.DocumentId
            WHERE d.DocumentId=@Id;
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        if (reader.IsDBNull(0))
            throw new InvalidOperationException(
                $"Receivable missing. Credit={reader.GetDecimal(6)}; document={reader.GetString(7)}; job={(reader.IsDBNull(8) ? "none" : reader.GetString(8))}; error={(reader.IsDBNull(9) ? "none" : reader.GetString(9))}");
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));
    }

    private async Task EnsureAccountAsync(SqlConnection connection,
        SqlTransaction transaction, string code, string name, string type,
        bool requiresParty)
    {
        await ExecuteAsync(connection, transaction, """
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts
                WHERE TenantId=@TenantId AND Code=@Code)
              INSERT dbo.AccountingAccounts(
                AccountId,TenantId,Code,Name,AccountType,AllowsPosting,
                RequiresParty,IsActive,CreatedAt)
              VALUES(NEWID(),@TenantId,@Code,@Name,@Type,1,
                @RequiresParty,1,SYSDATETIMEOFFSET());
            """,
            new("@TenantId", fixture.TenantId), new("@Code", code),
            new("@Name", name), new("@Type", type),
            new("@RequiresParty", requiresParty));
    }

    private async Task AssertCommercialOnlyProcessingAsync(Guid documentId)
    {
        Assert.Equal(AccountingPostingStatuses.CommercialEffectsApplied,
            await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
                documentId));
        Assert.False(await ScalarAsync<bool>(
            "SELECT AccountingEntryRequired FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",
            documentId));
        Assert.Equal(0, await CountAsync(
            "AccountingEntries", "SourceDocumentId", documentId));
    }

    private async Task ReprocessAsync(Guid documentId, string documentType)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>()
            .ProcessAsync(documentId, documentType, fixture.BusinessId,
                CancellationToken.None);
    }

    private async Task<AccountingSettingsState> DisableAccountingAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var read = new SqlCommand("""
            SELECT Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,
              ActivationRequestedAt,ActivationRequestedByUserId,ActivatedAt,ActivatedByUserId
            FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId;
            """, connection, (SqlTransaction)transaction))
        {
            read.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var state = new AccountingSettingsState(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7));
            await reader.DisposeAsync();

            await using var command = new SqlCommand("""
                UPDATE dbo.AccountingTenantSettings
                SET Status=N'Disabled',EffectiveFrom=NULL,OpeningBalanceMode=NULL,
                    ActivationRequestedAt=NULL,ActivationRequestedByUserId=NULL,
                    ActivatedAt=NULL,ActivatedByUserId=NULL,UpdatedAt=SYSDATETIMEOFFSET()
                WHERE TenantId=@TenantId;
                """, connection, (SqlTransaction)transaction);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            await transaction.CommitAsync();
            return state;
        }
    }

    private async Task RestoreAccountingAsync(AccountingSettingsState state)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.AccountingTenantSettings
            SET Status=@Status,FunctionalCurrencyCode=@Currency,
                EffectiveFrom=@EffectiveFrom,OpeningBalanceMode=@OpeningMode,
                ActivationRequestedAt=@RequestedAt,
                ActivationRequestedByUserId=@RequestedBy,
                ActivatedAt=@ActivatedAt,ActivatedByUserId=@ActivatedBy,
                UpdatedAt=SYSDATETIMEOFFSET()
            WHERE TenantId=@TenantId;
            """;
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Status", state.Status);
        command.Parameters.AddWithValue("@Currency", state.FunctionalCurrencyCode);
        command.Parameters.AddWithValue("@EffectiveFrom",
            state.EffectiveFrom?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@OpeningMode", state.OpeningBalanceMode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@RequestedAt", state.ActivationRequestedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@RequestedBy", state.ActivationRequestedByUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ActivatedAt", state.ActivatedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ActivatedBy", state.ActivatedByUserId ?? (object)DBNull.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed record AccountingSettingsState(
        string Status,
        string FunctionalCurrencyCode,
        DateOnly? EffectiveFrom,
        string? OpeningBalanceMode,
        DateTimeOffset? ActivationRequestedAt,
        Guid? ActivationRequestedByUserId,
        DateTimeOffset? ActivatedAt,
        Guid? ActivatedByUserId);

    private async Task EnsureMappingAsync(SqlConnection connection,
        SqlTransaction transaction, string category, string code)
    {
        await ExecuteAsync(connection, transaction, """
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings
                WHERE TenantId=@TenantId AND BusinessId IS NULL
                  AND Category=@Category AND EffectiveFrom='2026-01-01')
              INSERT dbo.AccountingAccountMappings(
                MappingId,TenantId,BusinessId,Category,AccountId,
                EffectiveFrom,CreatedAt)
              SELECT NEWID(),@TenantId,NULL,@Category,AccountId,
                '2026-01-01',SYSDATETIMEOFFSET()
              FROM dbo.AccountingAccounts
              WHERE TenantId=@TenantId AND Code=@Code;
            """, new("@TenantId", fixture.TenantId),
            new("@Category", category), new("@Code", code));
    }

    private static async Task ExecuteAsync(SqlConnection connection,
        SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(
        HttpClient client, string url, T body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private Task<int> CountAsync(string table, string column, Guid id)
    {
        Assert.Contains($"{table}:{column}", new[]
        {
            "SalesPayments:DocumentId",
            "ReceivableTransactions:SourceDocumentId",
            "AccountingEntries:SourceDocumentId",
            "AccountingPostingJobs:SourceDocumentId",
            "CustomerPaymentApplications:PaymentId",
            "ServerOutboxMessages:DocumentId"
        });
        return ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id", id);
    }

    private async Task<decimal> AccountAmountAsync(
        Guid documentId, string code, bool debit)
    {
        Assert.Contains(code, new[] { "110505", "130505" });
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
        command.Parameters.AddWithValue("@Code", code);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private sealed record ReceivableEvidence(
        Guid ReceivableId, Guid CustomerId, decimal OriginalAmount,
        decimal OutstandingAmount, string Status, DateTimeOffset DueDate);
}
