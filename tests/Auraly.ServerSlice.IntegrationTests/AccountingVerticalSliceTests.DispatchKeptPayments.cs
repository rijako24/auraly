using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData("SalesReceipt","Cash")]
    [InlineData("SalesReceipt","Transfer")]
    [InlineData("SalesReceipt","DebitCard")]
    [InlineData("SalesReceipt","CreditCard")]
    [InlineData("SalesInvoice","Cash")]
    [InlineData("SalesInvoice","Transfer")]
    [InlineData("SalesInvoice","DebitCard")]
    [InlineData("SalesInvoice","CreditCard")]
    public async Task Keeping_the_order_payment_preserves_its_tender_without_transport_cash_or_another_journal(string type,string method)
    {
        using var admin=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,CatalogPermissionCodes.Update,DispatchPermissionCodes.Settle);
        using(var defaults=await admin.PutAsync("/api/commerce/v1/accounting/defaults",null))defaults.EnsureSuccessStatusCode();
        using(var activation=await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))activation.EnsureSuccessStatusCode();
        var form=await CreateClosureFormAsync(admin,method);
        var card=method is "DebitCard" or "CreditCard";
        var franchise=card?(await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code:null;
        var issuer=Guid.NewGuid();var issuerSession=Guid.NewGuid();var order=Guid.NewGuid();
        await fixture.SeedConfirmedOrdersForInvoiceAsync(issuer,issuerSession,order,Guid.NewGuid());
        using var cashier=fixture.CreateUserClient(issuer,"sales.create","orders.read","orders.recover","orders.invoice");
        using var invoiceRequest=new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/orders/invoice"){
            Content=JsonContent.Create(new InvoiceOrdersRequest(issuerSession,fixture.WarehouseId,issuer,[order],method,method=="Transfer"?"ORIGINAL-REF":null,
                DocumentType:type,PaymentMethodId:form.PaymentMethodId,PaymentMethodVersion:form.Version,PaymentFormCode:form.Code,
                PaymentFormName:form.Name,AccountId:form.AccountId,CardFranchiseCode:franchise,ApprovalNumber:card?"ORIGINAL-APR":null))};
        invoiceRequest.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
        using var invoiced=await cashier.SendAsync(invoiceRequest);
        Assert.True(invoiced.IsSuccessStatusCode,await invoiced.Content.ReadAsStringAsync());
        var result=Assert.Single((await invoiced.Content.ReadFromJsonAsync<InvoiceOrdersResponse>())!.Results);
        Assert.Equal("Invoiced",result.Status);var sale=result.DocumentId!.Value;
        var amount=await ScalarAsync<decimal>("SELECT PayableAmount FROM dbo.SalesDocuments WHERE DocumentId=@Id",sale);
        var paymentHash=await ScalarAsync<string>("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM dbo.SalesPayments WHERE DocumentId=@Id FOR JSON PATH)),2)",sale);
        Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id",sale));
        Assert.Equal(issuerSession,await ScalarAsync<Guid>("SELECT WorkSessionId FROM dbo.WorkSessionMovements WHERE DocumentId=@Id",sale));
        Assert.Equal(method,await ScalarAsync<string>("SELECT PaymentMethodCode FROM dbo.WorkSessionMovements WHERE DocumentId=@Id",sale));
        Assert.Equal(amount,await ScalarAsync<decimal>("SELECT Amount FROM dbo.WorkSessionMovements WHERE DocumentId=@Id",sale));
        var originalSession=await ScalarAsync<string>("SELECT (SELECT * FROM dbo.WorkSessionMovements WHERE WorkSessionId=@Id ORDER BY WorkSessionMovementId FOR JSON PATH,INCLUDE_NULL_VALUES)",issuerSession);
        var dispatch=Guid.NewGuid();var source=Guid.NewGuid();
        var previous=await SeedDispatchSettlementAsync(dispatch,source,Guid.NewGuid(),sale,amount);
        try
        {
            await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
            await ResetSeededDispatchForDeliveryAsync(connection,dispatch);
            using var driver=fixture.CreateUserClient(fixture.UserId,DispatchPermissionCodes.ExecuteDeliveries);
            var url=$"/api/commerce/v1/dispatches/{dispatch}/delivery-results";
            var kept=new DispatchDeliveryPaymentInput("KeepCurrent",null,0,null,null);
            var request=new RecordDispatchDeliveryRequest(source,"Delivered",null,null,null,null,DateTimeOffset.UtcNow,
                Guid.NewGuid().ToString("N"),[kept],[]);
            foreach(var invalid in new[]{request with{Payments=[kept with{Amount=1}]},request with{Payments=[kept with{PaymentMethodId=form.PaymentMethodId,PaymentMethodVersion=form.Version}]},
                request with{Payments=[kept,new("InvoicePayment",method,amount,method=="Transfer"?"OTHER":null,null,form.PaymentMethodId,form.Version,franchise,card?"OTHER":null)]}})
            {
                using var rejected=await driver.PutAsJsonAsync(url,invalid);Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
            }
            for(var replay=0;replay<2;replay++)
            {
                using var delivered=await driver.PutAsJsonAsync(url,request);Assert.True(delivered.IsSuccessStatusCode,await delivered.Content.ReadAsStringAsync());
                var payment=Assert.Single(Assert.Single((await delivered.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!.Documents).Payments);
                Assert.Equal("KeepCurrent",payment.ApplicationType);Assert.Equal(0,payment.Amount);Assert.Null(payment.PaymentMethod);
            }
            using(var closed=await driver.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/close-route",new CloseDispatchRouteRequest(0,null,Guid.NewGuid().ToString("N"),0,0)))
                Assert.True(closed.IsSuccessStatusCode,await closed.Content.ReadAsStringAsync());
            Assert.Equal(0,await ScalarAsync<decimal>("SELECT ExpectedCash FROM dispatch.Closures WHERE DispatchId=@Id",dispatch));
            Assert.Equal(amount,await ScalarAsync<decimal>("SELECT PreviouslyCollectedTotal FROM dispatch.PaymentTotals(@Id)",dispatch));
            var worker=ActivatorUtilities.CreateInstance<DispatchSettlementHostedService>(fixture.Services);await worker.StartAsync(default);
            try
            {
                var settle=new SettleDispatchRequest(0,null,$"keep-current:{dispatch:N}");
                using(var accepted=await admin.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/settle",settle))
                    Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
                var deadline=DateTimeOffset.UtcNow.AddSeconds(30);
                while(await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id",dispatch)!="Closed"&&DateTimeOffset.UtcNow<deadline)await Task.Delay(50);
                Assert.Equal("Closed",await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id",dispatch));
                using(var replay=await admin.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/settle",settle))replay.EnsureSuccessStatusCode();
                Assert.Equal(paymentHash,await ScalarAsync<string>("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM dbo.SalesPayments WHERE DocumentId=@Id FOR JSON PATH)),2)",sale));
                Assert.Equal(originalSession,await ScalarAsync<string>("SELECT (SELECT * FROM dbo.WorkSessionMovements WHERE WorkSessionId=@Id ORDER BY WorkSessionMovementId FOR JSON PATH,INCLUDE_NULL_VALUES)",issuerSession));
                Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",sale));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE WorkSessionId=(SELECT WorkSessionId FROM dbo.WorkSessions WHERE DispatchId=@Id)",dispatch));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPayments WHERE IdempotencyKey LIKE CONCAT(N'dispatch-settlement:',LOWER(REPLACE(CONVERT(nvarchar(36),@Id),N'-',N'')),N':%')",dispatch));
                using var scope=fixture.CreateScope();var reporting=scope.ServiceProvider.GetRequiredService<Auraly.Infrastructure.Persistence.SqlSalesReportingProcessor>();
                await reporting.ProcessAsync(sale,type,fixture.BusinessId,1,default);
                var closure=await ScalarAsync<Guid>("SELECT DispatchSettlementId FROM dispatch.Closures WHERE DispatchId=@Id",dispatch);
                Assert.Equal(0,await CountAsync("AccountingEntries","SourceDocumentId",closure));
                for(var replay=0;replay<2;replay++)await reporting.ProcessAsync(closure,"DispatchCashDifference",fixture.BusinessId,1,default);
                Assert.Equal(amount,await ScalarAsync<decimal>("SELECT CollectedAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Id",sale));
                Assert.Equal(method,await ScalarAsync<string>("SELECT MethodCode FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'Payment'",sale));
                Assert.Equal(issuerSession,await ScalarAsync<Guid>("SELECT WorkSessionId FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'Payment'",sale));
                using var historyClient=fixture.CreateAdminClient("sales.reprint");
                var number=await ScalarAsync<string>("SELECT DocumentNumber FROM dbo.SalesDocuments WHERE DocumentId=@Id",sale);
                using var history=await historyClient.PostAsJsonAsync("/api/commerce/v1/pos/drafts/sales/search",new SearchOnlineSalesIssuedSalesRequest(new(fixture.BusinessId),number,Take:10,DateRangeVersion:2));
                history.EnsureSuccessStatusCode();var invoice=Assert.Single((await history.Content.ReadFromJsonAsync<OnlineSalesIssuedSalePage>())!.Items);
                var preserved=Assert.Single(invoice.Payments!);Assert.Equal(method,preserved.MethodCode);Assert.Equal(amount,preserved.Amount);Assert.Equal(form.Name,preserved.FormName);
            }
            finally{await worker.StopAsync(default);worker.Dispose();}
        }
        finally{await RestoreShortageAccountAsync(previous);}
    }
}
