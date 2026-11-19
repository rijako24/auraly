using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class OnlineSalesCheckoutTests
{
    [Theory]
    [InlineData(PosSaleDocumentTypes.Invoice)]
    [InlineData(PosSaleDocumentTypes.Receipt)]
    public async Task A_configured_transfer_uses_its_real_bank_for_sales_and_replay_preserves_one_balanced_journal(string documentType)
    {
        var userId = await CreateUserAsync("native-transfer");
        using var client = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCreate,
            CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        var bankType = (await client.GetFromJsonAsync<ReferenceOption[]>(
            "/api/commerce/v1/reference-options/bank-account-type"))!.Single(value => value.Code == "Checking");
        var bankId = Guid.NewGuid();
        using (var bank = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
            new SaveBankAccountRequest(bankId, null, bankType.Id, "Banco de transferencia configurada",
                bankId.ToString("N"), "Transferencia de prueba", false, true, null)))
            Assert.True(bank.IsSuccessStatusCode, await bank.Content.ReadAsStringAsync());
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(
            fixture, client, "1110", "Asset", "Bank", "Debit", bankId);
        var type = (await client.GetFromJsonAsync<ReferenceOption[]>(
            "/api/commerce/v1/reference-options/payment-method"))!.Single(value => value.Code == "Transfer");
        var configured = new SavePaymentMethodRequest(Guid.NewGuid(), $"BANK-{Guid.NewGuid():N}",
            "Transferencia compartida", type.Id, account.AccountId, true, true, true, true, true, 40,
            UseForVouchers: true);
        using var saved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/payment-methods/{configured.PaymentMethodId}", configured);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var form = (await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        var captured = await CaptureAsync(client, await OpenAsync(client));
        var adjustment = PosPaymentRoundingPolicy.Adjustment(captured.PayableAmount);
        var request = new CompleteOnlineSalesDraftRequest(captured.Version,
            [new("Transfer", captured.PayableAmount, "NATIVE-BANK", BankAccountId: bankId,
                RoundingAdjustment: adjustment, PaymentMethodId: form.PaymentMethodId,
                PaymentMethodVersion: form.Version, PaymentMethodCode: form.Code,
                PaymentMethodName: form.Name, AccountId: form.AccountId)], DocumentType: documentType);
        var key = $"native-bank-{Guid.NewGuid():N}";
        var completed = await CompleteAsync(client, captured.DraftId, request, key);
        var replay = await CompleteAsync(client, captured.DraftId, request, key);
        Assert.Equal(completed.Receipt.DocumentId, replay.Receipt.DocumentId);
        Assert.Equal(form.PaymentMethodId, Assert.Single(completed.Receipt.Payments).PaymentMethodId);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT COUNT(DISTINCT entry.EntryId),SUM(line.Debit-line.Credit),
              SUM(CASE WHEN line.AccountId=@Account THEN line.Debit ELSE 0 END),
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Document),
              (SELECT COUNT(*) FROM dbo.Receivables WHERE SourceDocumentId=@Document)
            FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Document;
            """, connection);
        verify.Parameters.AddWithValue("@Document", completed.Receipt.DocumentId);
        verify.Parameters.AddWithValue("@Account", account.AccountId);
        await using var result = await verify.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync()); Assert.Equal(1, result.GetInt32(0));
        Assert.Equal(0m, result.GetDecimal(1)); Assert.Equal(captured.PayableAmount + adjustment, result.GetDecimal(2));
        Assert.Equal(1, result.GetInt32(3)); Assert.Equal(0, result.GetInt32(4));
    }

    [Theory]
    [InlineData(PosSaleDocumentTypes.Invoice)]
    [InlineData(PosSaleDocumentTypes.Receipt)]
    public async Task Configured_money_and_automatic_financing_create_one_receivable_without_an_extra_credit_form(string documentType)
    {
        var userId=await CreateUserAsync("automatic-financing");
        var (customer,site)=await CreateCustomerAsync(userId,false,"Cliente de financiación automática");
        using var client=fixture.CreateUserClient(userId,CommercePermissionCodes.SalesCreate,
            ReceivablesPermissionCodes.ManageCredit,CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,
            AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure);
        using(var profile=await client.PutAsJsonAsync($"/api/commerce/v1/customers/{customer}/credit",
            new UpdateCustomerCreditProfileRequest(fixture.BusinessId,1_000_000m,30,true)))
            Assert.True(profile.IsSuccessStatusCode,await profile.Content.ReadAsStringAsync());
        var cash=await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var type=(await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!
            .Single(value=>value.Code=="Cash");
        var configured=new SavePaymentMethodRequest(Guid.NewGuid(),$"AUTO-{Guid.NewGuid():N}","Caja de financiación automática",
            type.Id,cash.AccountId,true,false,false,false,true,10);
        using var saved=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configured.PaymentMethodId}",configured);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var form=(await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        var draft=await OpenAsync(client);
        using(var select=new HttpRequestMessage(HttpMethod.Put,$"/api/commerce/v1/pos/drafts/{draft.DraftId}/customer")
            {Content=JsonContent.Create(new SelectOnlineSalesDraftCustomerRequest(customer,draft.Version,site))})
        {
            select.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
            using var response=await client.SendAsync(select);
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            draft=(await response.Content.ReadFromJsonAsync<OnlineSalesCustomerSelection>())!.Draft;
        }
        var captured=await CaptureAsync(client,draft);
        var total=PosPaymentRoundingPolicy.RoundedTotal(captured.PayableAmount);
        var financed=total/2;
        var adjustment=PosPaymentRoundingPolicy.Adjustment(captured.PayableAmount);
        var request=new CompleteOnlineSalesDraftRequest(captured.Version,
            [new("Cash",total-financed-adjustment,null,RoundingAdjustment:adjustment,
                PaymentMethodId:form.PaymentMethodId,PaymentMethodVersion:form.Version,
                PaymentMethodCode:form.Code,PaymentMethodName:form.Name,AccountId:form.AccountId)],
            new(financed),documentType);
        var key=$"automatic-financing-{Guid.NewGuid():N}";
        var validationModes = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        using var validationReads = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains(
                "La forma de pago de la venta no coincide", StringComparison.Ordinal),
            beforeExecute: command => validationModes.Enqueue(Convert.ToBoolean(command.Parameters["@Current"].Value)));
        var completed=await CompleteAsync(client,captured.DraftId,request,key);
        // Preparation checks current eligibility before assigning a number;
        // reception checks the identity of that accepted fact in its own transaction.
        Assert.Equal(new[] { true, false }, validationModes.ToArray());
        var replay=await CompleteAsync(client,captured.DraftId,request,key);
        Assert.Equal(2, validationReads.Count);
        Assert.Equal(new[] { true, false }, validationModes.ToArray());
        Assert.Equal(completed.Receipt.DocumentId,replay.Receipt.DocumentId);
        var credit=Assert.Single(completed.Receipt.Payments.Where(payment=>payment.MethodCode=="Credit"));
        Assert.Equal(financed,credit.Amount); Assert.Null(credit.PaymentMethodId);
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify=new SqlCommand("""
            SELECT receivable.OutstandingAmount,opening.AccountId,configured.AccountId,
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@Id AND PaymentMethodId=@Form)
            FROM dbo.Receivables receivable
            JOIN dbo.AccountingEntryLines opening ON opening.EntryId=receivable.OpeningEntryId
              AND opening.LineNumber=receivable.OpeningEntryLineNumber
            JOIN dbo.AccountingEntries entry ON entry.EntryId=opening.EntryId
            CROSS APPLY accounting.ConfiguredPostingAccountIdentity(@Tenant,@Business,N'AccountsReceivable',CAST(entry.OccurredAt AS date)) configured
            WHERE receivable.BusinessId=@Business AND receivable.SourceDocumentId=@Id;
            """,connection);
        verify.Parameters.AddWithValue("@Id",completed.Receipt.DocumentId); verify.Parameters.AddWithValue("@Form",form.PaymentMethodId);
        verify.Parameters.AddWithValue("@Tenant",fixture.TenantId); verify.Parameters.AddWithValue("@Business",fixture.BusinessId);
        await using var result=await verify.ExecuteReaderAsync(); Assert.True(await result.ReadAsync());
        Assert.Equal(financed,result.GetDecimal(0)); Assert.Equal(result.GetGuid(2),result.GetGuid(1));
        for(var index=3;index<7;index++) Assert.Equal(1,result.GetInt32(index));
        Assert.False(await result.ReadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incomplete_financing_form_is_rejected_before_numbering(bool accountOnly)
    {
        var userId = await CreateUserAsync("financing-form-identity");
        using var client = fixture.CreateUserClient(userId,CommercePermissionCodes.SalesCreate);
        var draft = await CaptureAsync(client,await OpenAsync(client));
        var before = await ReadCursorValuesAsync();
        var terms = new OnlineSalesCreditTerms(draft.PayableAmount,
            PaymentMethodId:accountOnly?null:Guid.NewGuid(),AccountId:accountOnly?Guid.NewGuid():null);
        using var request = Mutation(draft.DraftId,new CompleteOnlineSalesDraftRequest(draft.Version,[],terms),Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Contains("identidad de las formas de pago",await response.Content.ReadAsStringAsync());
        Assert.Equal(before,await ReadCursorValuesAsync());
    }

    [Theory]
    [InlineData(PosSaleDocumentTypes.Invoice, false)]
    [InlineData(PosSaleDocumentTypes.Receipt, true)]
    public async Task Configured_financing_resolves_its_current_account_after_acceptance_without_becoming_collected_money(string documentType, bool mixed)
    {
        var userId = await CreateUserAsync("configured-financing");
        var (customer,site) = await CreateCustomerAsync(userId,false,"Cliente con financiación configurada");
        using var client = fixture.CreateUserClient(userId,CommercePermissionCodes.SalesCreate,
            ReceivablesPermissionCodes.Read,ReceivablesPermissionCodes.ManageCredit,CatalogPermissionCodes.Read,
            CatalogPermissionCodes.Update,AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,AccountingPermissionCodes.Retry);
        using(var profile = await client.PutAsJsonAsync($"/api/commerce/v1/customers/{customer}/credit",
            new UpdateCustomerCreditProfileRequest(fixture.BusinessId,1_000_000m,30,true)))
            Assert.True(profile.IsSuccessStatusCode,await profile.Content.ReadAsStringAsync());
        var before = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1397","Asset","Receivable","Debit");
        var current = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1398","Asset","Receivable","Debit");
        var types = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var configuration = new SavePaymentMethodRequest(Guid.NewGuid(),$"CRE-{Guid.NewGuid():N}","Crédito anterior",
            types.Single(type=>type.Code=="Credit").Id,before.AccountId,true,false,false,false,true,10);
        using var saved = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}",configuration);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var form = (await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        PaymentMethodView? cashForm = null;
        if(mixed)
        {
            var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
            var request = new SavePaymentMethodRequest(Guid.NewGuid(),$"CASH-{Guid.NewGuid():N}","Caja de venta mixta",
                types.Single(type=>type.Code=="Cash").Id,cash.AccountId,true,false,false,false,true,10);
            using var response = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}",request);
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            cashForm = (await response.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        }
        var draft = await OpenAsync(client);
        using(var select = new HttpRequestMessage(HttpMethod.Put,$"/api/commerce/v1/pos/drafts/{draft.DraftId}/customer")
            {Content=JsonContent.Create(new SelectOnlineSalesDraftCustomerRequest(customer,draft.Version,site))})
        {
            select.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
            using var response = await client.SendAsync(select);
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            draft = (await response.Content.ReadFromJsonAsync<OnlineSalesCustomerSelection>())!.Draft;
        }
        var captured = await CaptureAsync(client,draft);
        var total = PosPaymentRoundingPolicy.RoundedTotal(captured.PayableAmount);
        var cashAmount = mixed ? total/2 : 0m;
        var credit = new OnlineSalesCreditTerms(total-cashAmount,form.PaymentMethodId,form.Version,form.Code,form.Name,form.AccountId);
        OnlineSalesPayment[] tender = cashForm is null ? [] : [new OnlineSalesPayment("Cash",
            cashAmount-PosPaymentRoundingPolicy.Adjustment(captured.PayableAmount),null,
            RoundingAdjustment:PosPaymentRoundingPolicy.Adjustment(captured.PayableAmount),
            PaymentMethodId:cashForm.PaymentMethodId,PaymentMethodVersion:cashForm.Version,PaymentMethodCode:cashForm.Code,
            PaymentMethodName:cashForm.Name,AccountId:cashForm.AccountId)];
        var checkout = new CompleteOnlineSalesDraftRequest(captured.Version,tender,credit,documentType);
        var key = $"configured-financing-{Guid.NewGuid():N}";
        Guid documentId;
        // Pause the existing posting path using its real closed-period gate.
        // Restore only the period identified in this isolated test database.
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var period = new SqlCommand("""
            DECLARE @Period uniqueidentifier;
            IF (SELECT COUNT(*) FROM dbo.AccountingPeriods WHERE TenantId=@Tenant AND Status=N'Open'
              AND StartsOn<=@Day AND EndsOn>=@Day)<>1
              THROW 51500,'The test requires one current open period.',1;
            SELECT @Period=PeriodId FROM dbo.AccountingPeriods WHERE TenantId=@Tenant AND Status=N'Open'
              AND StartsOn<=@Day AND EndsOn>=@Day;
            UPDATE dbo.AccountingPeriods SET Status=N'Closed' WHERE PeriodId=@Period; SELECT @Period;
            """,connection);
        period.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        using var clockScope = fixture.CreateScope();
        period.Parameters.AddWithValue("@Day",clockScope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime.Date);
        var periodId = (Guid)(await period.ExecuteScalarAsync())!;
        try
        {
            documentId = (await CompleteAsync(client,captured.DraftId,checkout,key)).Receipt.DocumentId;
            await using var pending = new SqlCommand("""
                SELECT sale.CreditAccountId,job.LastErrorCode FROM dbo.SalesDocuments sale
                JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=sale.DocumentId AND job.BusinessId=sale.BusinessId
                WHERE sale.DocumentId=@Id AND job.SourceDocumentType=@Type;
                """,connection);
            pending.Parameters.AddWithValue("@Id",documentId);
            pending.Parameters.AddWithValue("@Type",documentType);
            await using(var row = await pending.ExecuteReaderAsync())
            {
                Assert.True(await row.ReadAsync());Assert.True(row.IsDBNull(0));Assert.Equal("OpenPeriodMissing",row.GetString(1));
            }
            form = Assert.Single((await client.GetFromJsonAsync<PaymentMethodPage>(
                $"/api/commerce/v1/payment-methods?paymentMethodId={form.PaymentMethodId}"))!.Items);
            using var semanticEdit = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
                configuration with { PaymentTypeId = types.Single(type => type.Code == "Cash").Id,
                    IsActive = false, RowVersion = form.RowVersion });
            Assert.Equal(HttpStatusCode.Conflict, semanticEdit.StatusCode);
            Assert.Contains("forma utilizada", await semanticEdit.Content.ReadAsStringAsync());
            using var changed = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
                configuration with {AccountId=current.AccountId,Name="Crédito vigente",RowVersion=form.RowVersion});
            Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
        }
        finally
        {
            await using var restore = new SqlCommand("UPDATE dbo.AccountingPeriods SET Status=N'Open' WHERE TenantId=@Tenant AND PeriodId=@Id",connection);
            restore.Parameters.AddWithValue("@Tenant",fixture.TenantId);restore.Parameters.AddWithValue("@Id",periodId);
            Assert.Equal(1,await restore.ExecuteNonQueryAsync());
        }
        using var resolutionReads = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("DECLARE @Resolved TABLE(Line int PRIMARY KEY",StringComparison.Ordinal));
        using(var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{documentId}/retry",null))
            Assert.True(retry.IsSuccessStatusCode,await retry.Content.ReadAsStringAsync());
        Assert.Equal(1,resolutionReads.Count);
        await using var verify = new SqlCommand("""
            SELECT sale.CreditAccountId,sale.CreditPaymentMethodVersion,receivable.OutstandingAmount,
              (SELECT SUM(line.Debit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@Id AND entry.BusinessId=@Business AND line.AccountId=sale.CreditAccountId),
              (SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@Id AND MethodCode=N'Credit'),
              (SELECT AccountId FROM dbo.AccountingEntryLines opening WHERE opening.EntryId=receivable.OpeningEntryId
                AND opening.LineNumber=receivable.OpeningEntryLineNumber)
            FROM dbo.SalesDocuments sale JOIN dbo.Receivables receivable ON receivable.SourceDocumentId=sale.DocumentId
              AND receivable.BusinessId=sale.BusinessId WHERE sale.DocumentId=@Id AND sale.BusinessId=@Business;
            """,connection);
        verify.Parameters.AddWithValue("@Id",documentId);verify.Parameters.AddWithValue("@Business",fixture.BusinessId);
        await using(var row = await verify.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());Assert.Equal(current.AccountId,row.GetGuid(0));Assert.Equal(2,row.GetInt32(1));
            Assert.Equal(credit.Amount,row.GetDecimal(2));Assert.Equal(credit.Amount,row.GetDecimal(3));
            Assert.Equal(mixed?1:0,row.GetInt32(4));Assert.Equal(0,row.GetInt32(5));
            Assert.Equal(current.AccountId,row.GetGuid(6));Assert.False(await row.ReadAsync());
        }
        var replay = await CompleteAsync(client,captured.DraftId,checkout,key);
        Assert.Equal(documentId,replay.Receipt.DocumentId);
        Assert.Equal(form.PaymentMethodId,Assert.Single(replay.Receipt.Payments.Where(payment=>payment.MethodCode=="Credit")).PaymentMethodId);
        Assert.Equal(1,resolutionReads.Count);
    }

    [Fact]
    public async Task Configured_sale_keeps_its_frozen_form_and_account_on_replay_after_catalog_edit()
    {
        var userId = await CreateUserAsync("configured-sale");
        using var client = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCreate,
            CatalogPermissionCodes.Read, CatalogPermissionCodes.Update);
        var accountId = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,
              IsActive,CreatedAt,FinancialFunction,BalanceSide)
            VALUES(@Id,@Tenant,@Code,N'Caja de venta configurada',N'Asset',1,0,1,SYSUTCDATETIME(),N'Cash',N'Debit');
            """, new("@Id", accountId), new("@Tenant", fixture.TenantId), new("@Code", $"SALE-{Guid.NewGuid():N}"[..30]));
        var type = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!
            .Single(value => value.Code == "Cash");
        var configuration = new SavePaymentMethodRequest(Guid.NewGuid(), $"SALE-{Guid.NewGuid():N}", "Caja de venta",
            type.Id, accountId, true, false, false, false, true, 10);
        using var saved = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}", configuration);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var form = (await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        var captured = await CaptureAsync(client, await OpenAsync(client));
        var request = new CompleteOnlineSalesDraftRequest(captured.Version, [new OnlineSalesPayment(
            "Cash", captured.PayableAmount, null, PaymentMethodId: form.PaymentMethodId,
            PaymentMethodVersion: form.Version, PaymentMethodCode: form.Code,
            PaymentMethodName: form.Name, AccountId: form.AccountId)]);
        var key = $"configured-sale-{Guid.NewGuid():N}";

        var accepted = await CompleteAsync(client, captured.DraftId, request, key);
        form = Assert.Single((await client.GetFromJsonAsync<PaymentMethodPage>(
            $"/api/commerce/v1/payment-methods?paymentMethodId={form.PaymentMethodId}"))!.Items);
        var creditType = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!
            .Single(value => value.Code == "Credit");
        using var semanticEdit = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}",
            configuration with { PaymentTypeId = creditType.Id, IsActive = false, RowVersion = form.RowVersion });
        Assert.Equal(HttpStatusCode.Conflict, semanticEdit.StatusCode);
        using var edited = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}",
            configuration with { Name = "Caja con otro nombre", RowVersion = form.RowVersion });
        Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());
        var replay = await CompleteAsync(client, captured.DraftId, request, key);

        Assert.Equal(accepted.Receipt.DocumentId, replay.Receipt.DocumentId);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT PaymentMethodId,PaymentMethodVersion,PaymentMethodName,AccountId
            FROM dbo.SalesPayments WHERE DocumentId=@Id;
            """, connection);
        verify.Parameters.AddWithValue("@Id", accepted.Receipt.DocumentId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(form.PaymentMethodId, reader.GetGuid(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal("Caja de venta", reader.GetString(2));
        Assert.Equal(accountId, reader.GetGuid(3));
        Assert.False(await reader.ReadAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Checkout_rejects_a_payment_account_or_version_without_a_configured_form(bool accountOnly)
    {
        var userId = await CreateUserAsync("payment-form-identity");
        using var client = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCreate);
        var captured = await CaptureAsync(client, await OpenAsync(client));
        var before = await ReadCursorValuesAsync();
        var payment = new OnlineSalesPayment("Cash", captured.PayableAmount, null,
            AccountId: accountOnly ? Guid.NewGuid() : null,
            PaymentMethodVersion: accountOnly ? null : 1);
        using var request = Mutation(captured.DraftId,
            new CompleteOnlineSalesDraftRequest(captured.Version, [payment]),
            $"payment-form-identity-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadCursorValuesAsync());
    }
}
