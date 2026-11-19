using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class DispatchDeliveryPaymentsTests
{
    [Fact]
    public async Task Approval_accepts_one_real_paid_expense_with_current_accounts_and_rejection_never_spends_cash()
    {
        using var admin=fixture.CreateAdminClient(AccountingPermissionCodes.Configure,AccountingPermissionCodes.Read,
            CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,ExpensePermissionCodes.Read,ExpensePermissionCodes.Configure,
            ExpensePermissionCodes.Create,ExpensePermissionCodes.Cancel,DispatchPermissionCodes.Settle,
            TaxationPermissionCodes.ManageWithholdingRules);
        using(var profile=await admin.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
            new SaveCounterpartyTaxProfileRequest(fixture.BusinessId,fixture.SupplierId,false,[],"11001")))
            Assert.True(profile.IsSuccessStatusCode,await profile.Content.ReadAsStringAsync());
        using var driver=fixture.CreateUserClient(fixture.UserId,DispatchPermissionCodes.ExecuteDeliveries);
        var cash=await CreateFormAsync("Cash");
        var receiptCash=await CreateFormAsync("Cash");
        async Task<PaymentMethodView> SaveCashAsync(Guid account)=>
            (await (await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{cash.PaymentMethodId}",
                new SavePaymentMethodRequest(cash.PaymentMethodId,cash.Code,cash.Name,cash.PaymentTypeId,account,true,true,true,false,true,10,cash.RowVersion)))
                .Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        cash=await SaveCashAsync(cash.AccountId);
        using var receiver=fixture.CreateUserClient(fixture.UserId,ExpensePermissionCodes.Read,ExpensePermissionCodes.Create,DispatchPermissionCodes.Settle);
        var receiverForms=(await receiver.GetFromJsonAsync<PaymentMethodPage>($"/api/commerce/v1/payment-methods/?usage=SupplierPayments&methodCode=Cash&paymentMethodId={cash.PaymentMethodId}&page=1&pageSize=10"))!;
        Assert.Equal(cash.PaymentMethodId,Assert.Single(receiverForms.Items).PaymentMethodId);
        using(var otherMethods=await receiver.GetAsync("/api/commerce/v1/payment-methods/?usage=SupplierPayments&methodCode=Transfer&page=1&pageSize=10"))Assert.Equal(HttpStatusCode.Forbidden,otherMethods.StatusCode);
        var before=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,admin,"5292","Expense","General","Debit");
        var current=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,admin,"6292","Cost","General","Debit");
        var conceptRequest=new SaveExpenseConceptRequest(Guid.NewGuid(),fixture.BusinessId,"Gasto de recorrido "+Guid.NewGuid().ToString("N")[..8],before.AccountId,null,null,true);
        using(var configured=await admin.PutAsJsonAsync($"/api/commerce/v1/expenses/concepts/{conceptRequest.ConceptId}",conceptRequest))
            Assert.True(configured.IsSuccessStatusCode,await configured.Content.ReadAsStringAsync());
        var options=(await driver.GetFromJsonAsync<ExpenseConceptOptionPage>($"/api/commerce/v1/expenses/concept-options?page=1&pageSize=10&search={Uri.EscapeDataString(conceptRequest.Name)}"))!;
        Assert.Equal(conceptRequest.ConceptId,Assert.Single(options.Items).ConceptId);
        using(var forbidden=await fixture.CreateUserClient(fixture.UserId,DispatchPermissionCodes.Read).GetAsync("/api/commerce/v1/expenses/concept-options?page=1&pageSize=10"))
            Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        var(dispatch,source,sale)=await SeedDeliveryAsync();
        var at=new DateTimeOffset(2026,11,17,16,0,0,TimeSpan.Zero);
        using(var delivered=await driver.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/delivery-results",
            new RecordDispatchDeliveryRequest(source,"Delivered",null,null,null,null,at,Guid.NewGuid().ToString("N"),
                [new("InvoicePayment","Cash",50000,null,null,receiptCash.PaymentMethodId,receiptCash.Version)],[])))
            Assert.True(delivered.IsSuccessStatusCode,await delivered.Content.ReadAsStringAsync());
        var report=new DispatchExpenseInput("Nombre que no sustituye al catálogo",1000,"Gasto de recorrido con soporte",null,Guid.NewGuid().ToString("N"),at,conceptRequest.ConceptId);
        var reportUrl=$"/api/commerce/v1/dispatches/{dispatch}/expenses";
        using var reported=await driver.PostAsJsonAsync(reportUrl,report);
        Assert.True(reported.IsSuccessStatusCode,await reported.Content.ReadAsStringAsync());
        var expense=Assert.Single((await reported.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!.Expenses);
        Assert.Equal(conceptRequest.Name,expense.Category);Assert.Equal(conceptRequest.ConceptId,expense.ConceptId);
        Assert.Null(expense.AcceptedExpenseId);Assert.Equal("Pending",expense.ApprovalStatus);
        using(var replay=await driver.PostAsJsonAsync(reportUrl,report))
            Assert.Equal(expense.ExpenseId,Assert.Single((await replay.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!.Expenses).ExpenseId);
        using(var changed=await driver.PostAsJsonAsync(reportUrl,report with{Amount=2000}))Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
        using var freeText=await driver.PostAsJsonAsync(reportUrl,report with{Category="Gasto descrito sin conexión",ConceptId=null,Amount=500,IdempotencyKey=Guid.NewGuid().ToString("N")});
        Assert.True(freeText.IsSuccessStatusCode,await freeText.Content.ReadAsStringAsync());
        var rejectedId=(await freeText.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!.Expenses.Single(item=>item.ExpenseId!=expense.ExpenseId).ExpenseId;
        using(var closed=await driver.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/close-route",new CloseDispatchRouteRequest(49000,null,Guid.NewGuid().ToString("N"),0,0)))
            Assert.True(closed.IsSuccessStatusCode,await closed.Content.ReadAsStringAsync());
        using(var late=await driver.PostAsJsonAsync(reportUrl,report with{IdempotencyKey=Guid.NewGuid().ToString("N")}))Assert.Equal(HttpStatusCode.Conflict,late.StatusCode);
        using(var noReason=await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/expenses/{rejectedId}/review",new ReviewDispatchExpenseRequest("Rejected",null,null,Guid.NewGuid().ToString("N"))))
            Assert.Equal(HttpStatusCode.BadRequest,noReason.StatusCode);
        var rejection=new ReviewDispatchExpenseRequest("Rejected",null,"El soporte no demuestra el gasto",Guid.NewGuid().ToString("N"));
        for(var attempt=0;attempt<2;attempt++)
        {
            using var rejected=await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/expenses/{rejectedId}/review",rejection);
            Assert.True(rejected.IsSuccessStatusCode,await rejected.Content.ReadAsStringAsync());
            Assert.Equal(50000m,(await rejected.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!.Settlement!.ExpectedCash);
        }
        using(var unlinkedApproval=await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/expenses/{expense.ExpenseId}/review",new ReviewDispatchExpenseRequest("Approved",1000,null,Guid.NewGuid().ToString("N"))))
            Assert.Equal(HttpStatusCode.BadRequest,unlinkedApproval.StatusCode);
        var request=new ConfirmExpenseRequest(Guid.NewGuid(),fixture.BusinessId,fixture.SupplierId,null,null,null,at,at,"COP","Gasto aprobado del recorrido",0,0,null,null,
            PurchaseEvidenceTypes.InternalReceiptVoucher,Lines:[new(null,conceptRequest.ConceptId,null,"Gasto del recorrido",1000,null,"CapitalizedCost",null)],
            DispatchExpenseId:expense.ExpenseId,DispatchId:dispatch,DispatchCashPaymentMethodId:cash.PaymentMethodId,DispatchCashPaymentMethodVersion:cash.Version);
        using var previewResponse=await admin.PostAsJsonAsync("/api/commerce/v1/expenses/preview",request);
        Assert.True(previewResponse.IsSuccessStatusCode,await previewResponse.Content.ReadAsStringAsync());
        var preview=(await previewResponse.Content.ReadFromJsonAsync<ExpensePreview>())!;
        Assert.True(preview.CanConfirm,string.Join(";",preview.Diagnostics));
        request=request with{CalculationHash=preview.CalculationHash};
        async Task<HttpResponseMessage> ConfirmAsync(HttpClient client)
        {
            using var message=new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/expenses/confirm"){Content=JsonContent.Create(request)};
            message.Headers.Add("Idempotency-Key",request.ExpenseId.ToString("N"));return await client.SendAsync(message);
        }
        using(var cannotApprove=await ConfirmAsync(driver))Assert.Equal(HttpStatusCode.Forbidden,cannotApprove.StatusCode);
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var period=new SqlCommand("""
            UPDATE dbo.AccountingPeriods SET Status=N'Closed' OUTPUT inserted.PeriodId
            WHERE TenantId=@Tenant AND Status=N'Open' AND StartsOn<=@Day AND EndsOn>=@Day;
            """,connection);
        period.Parameters.AddWithValue("@Tenant",fixture.TenantId);period.Parameters.AddWithValue("@Day",at.Date);
        var periodId=(Guid)(await period.ExecuteScalarAsync())!;
        ExpenseAcceptance accepted;
        string captured;
        var nextCash=await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,admin);
        async Task<string> SourceAsync(){await using var query=new SqlCommand("SELECT PayloadJson FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'",connection);query.Parameters.AddWithValue("@Id",request.ExpenseId);return (string)(await query.ExecuteScalarAsync())!;}
        try
        {
        using var acceptedResponse=await ConfirmAsync(receiver);
        Assert.True(acceptedResponse.IsSuccessStatusCode,await acceptedResponse.Content.ReadAsStringAsync());
        accepted=(await acceptedResponse.Content.ReadFromJsonAsync<ExpenseAcceptance>())!;
        Assert.NotNull(accepted.DispatchApproval);Assert.Equal(50000-preview.Withholding.NetAmount,accepted.DispatchApproval.ExpectedCash);
        captured=await SourceAsync();
        cash=await SaveCashAsync(nextCash.AccountId);
        using(var reconfigured=await admin.PutAsJsonAsync($"/api/commerce/v1/expenses/concepts/{conceptRequest.ConceptId}",conceptRequest with{ExpenseAccountId=current.AccountId}))
            Assert.True(reconfigured.IsSuccessStatusCode,await reconfigured.Content.ReadAsStringAsync());
        }
        finally
        {
            await using var reopen=new SqlCommand("UPDATE dbo.AccountingPeriods SET Status=N'Open' WHERE PeriodId=@Period",connection);
            reopen.Parameters.AddWithValue("@Period",periodId);await reopen.ExecuteNonQueryAsync();
        }
        using(var scope=fixture.CreateScope())
        {
            var processor=scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
            await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
            await processor.ProcessAsync(request.ExpenseId,ExpenseDocumentTypes.Expense,fixture.BusinessId,default);
        }
        Assert.Equal(captured,await SourceAsync());
        using(var replay=await ConfirmAsync(admin))
        {
            Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
            var replayed=(await replay.Content.ReadFromJsonAsync<ExpenseAcceptance>())!;Assert.True(replayed.IdempotentReplay);Assert.Equal(accepted.DispatchApproval,replayed.DispatchApproval);
        }
        await using var verify=new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.Expenses WHERE ExpenseId=@Id),
              (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Expense'),
              (SELECT Status FROM dbo.Expenses WHERE ExpenseId=@Id);
            SELECT line.AccountId,line.Debit,line.Credit FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
              WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'Expense';
            """,connection);verify.Parameters.AddWithValue("@Id",request.ExpenseId);
        await using(var reader=await verify.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());Assert.Equal(1,reader.GetInt32(0));Assert.Equal(0,reader.GetInt32(1));Assert.Equal(1,reader.GetInt32(2));Assert.Equal("Processed",reader.GetString(3));
            await reader.NextResultAsync();var lines=new List<(Guid Account,decimal Debit,decimal Credit)>();while(await reader.ReadAsync())lines.Add((reader.GetGuid(0),reader.GetDecimal(1),reader.GetDecimal(2)));
            Assert.Equal(lines.Sum(line=>line.Debit),lines.Sum(line=>line.Credit));Assert.Contains(lines,line=>line.Account==current.AccountId&&line.Debit==1000);
            Assert.Contains(lines,line=>line.Account==nextCash.AccountId&&line.Credit==preview.Withholding.NetAmount);Assert.DoesNotContain(lines,line=>line.Account==before.AccountId);
        }
        var detail=(await admin.GetFromJsonAsync<ExpenseDetail>($"/api/commerce/v1/expenses/{request.ExpenseId}"))!;
        Assert.Equal(dispatch,detail.SourceDispatchId);Assert.Null(detail.Payable);
        var page=(await admin.GetFromJsonAsync<ExpensePage>($"/api/commerce/v1/expenses?page=1&pageSize=25&search={accepted.DocumentNumber}&payableStatus=Paid"))!;
        Assert.Equal(request.ExpenseId,Assert.Single(page.Items).ExpenseId);
        var cardAccount=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,admin,"1397","Asset","CardClearing","Debit");
        var cardType=(await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!.Single(type=>type.Code=="DebitCard");
        using(var changeUsedType=await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{cash.PaymentMethodId}",
            new SavePaymentMethodRequest(cash.PaymentMethodId,cash.Code,cash.Name,cardType.Id,cardAccount.AccountId,true,true,true,false,true,10,cash.RowVersion)))
            Assert.Equal(HttpStatusCode.Conflict,changeUsedType.StatusCode);
        output.WriteLine("Report/rejection/approval/replay: one paid Expense, current cost and cash accounts, no duplicate payable or journal.");
    }
}
