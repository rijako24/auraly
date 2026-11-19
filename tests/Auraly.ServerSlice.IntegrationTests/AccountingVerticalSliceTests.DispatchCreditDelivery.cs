using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Fact]
    public async Task Credit_delivery_keeps_its_credit_terms_and_obligation_without_collecting_or_creating_a_payment()
    {
        using var admin=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,DispatchPermissionCodes.Settle);
        using(var defaults=await admin.PutAsync("/api/commerce/v1/accounting/defaults",null))defaults.EnsureSuccessStatusCode();
        using(var activation=await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))activation.EnsureSuccessStatusCode();
        var(customer,site)=await CreateCustomerAsync();
        var sale=WithUblSnapshot(fixture.CreateValidRequest(9896) with{CustomerId=customer,CustomerPartySiteId=site,Payments=[],
            Credit=new PosSaleCreditContract(customer,11900,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.FromHours(-5)),PartySiteId:site)});
        sale=sale with{UblSnapshot=sale.UblSnapshot! with{PaymentFormCode="2",DueDate=new(2026,8,31)}};
        using(var upload=fixture.CreateUploadMessage(sale))using(var response=await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await AssertBalancedAsync(sale.DocumentId);
        var dispatch=Guid.NewGuid();var source=Guid.NewGuid();var settlement=Guid.NewGuid();
        var previous=await SeedDispatchSettlementAsync(dispatch,source,settlement,sale.DocumentId,11900);
        try
        {
            await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
            await using(var setup=new SqlCommand("""
                DELETE total FROM dbo.WorkSessionClosurePaymentTotals total JOIN dispatch.Closures closure ON closure.DispatchSettlementId=total.WorkSessionClosureId WHERE closure.DispatchId=@Dispatch;
                DELETE closure FROM dbo.WorkSessionClosures closure JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId WHERE session.DispatchId=@Dispatch;
                DELETE dbo.WorkSessions WHERE DispatchId=@Dispatch;
                DELETE dbo.DispatchDeliveryPayments WHERE DispatchId=@Dispatch;
                UPDATE dbo.Dispatches SET Status=N'InDelivery' WHERE DispatchId=@Dispatch;
                """,connection)){setup.Parameters.AddWithValue("@Dispatch",dispatch);await setup.ExecuteNonQueryAsync();}
            using var driver=fixture.CreateUserClient(fixture.UserId,DispatchPermissionCodes.ExecuteDeliveries);
            var signed=new DispatchDeliveryPaymentInput("CreditDocument",null,0,null,"https://qa.invalid/factura-firmada.png");
            var request=new RecordDispatchDeliveryRequest(source,"Delivered",null,null,null,null,
                new DateTimeOffset(2026,11,17,16,0,0,TimeSpan.Zero),Guid.NewGuid().ToString("N"),[signed],[]);
            var url=$"/api/commerce/v1/dispatches/{dispatch}/delivery-results";
            foreach(var application in new[]{"CreditAdvance","InvoicePayment"})
            {
                using var attempted=await driver.PutAsJsonAsync(url,request with{Payments=[signed,new(application,"Cash",1000,null,null)]});
                Assert.Equal(HttpStatusCode.BadRequest,attempted.StatusCode);Assert.Contains("conserva su condición de crédito",await attempted.Content.ReadAsStringAsync());
            }
            using(var notDelivered=await driver.PutAsJsonAsync(url,request with{DeliveryStatus="NotDelivered",Reason="Cliente ausente",Payments=[],IdempotencyKey=Guid.NewGuid().ToString("N")}))
                Assert.True(notDelivered.IsSuccessStatusCode,await notDelivered.Content.ReadAsStringAsync());
            Assert.Equal(11900,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId));
            using(var delivered=await driver.PutAsJsonAsync(url,request))
            {
                Assert.True(delivered.IsSuccessStatusCode,await delivered.Content.ReadAsStringAsync());
                var execution=(await delivered.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!;
                var payment=Assert.Single(Assert.Single(execution.Documents).Payments);Assert.Equal("CreditDocument",payment.ApplicationType);Assert.Null(payment.PaymentMethod);Assert.Equal(0,payment.Amount);
            }
            using(var closed=await driver.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/close-route",new CloseDispatchRouteRequest(0,null,Guid.NewGuid().ToString("N"),0,0)))
                Assert.True(closed.IsSuccessStatusCode,await closed.Content.ReadAsStringAsync());
            var worker=ActivatorUtilities.CreateInstance<DispatchSettlementHostedService>(fixture.Services);await worker.StartAsync(default);
            try
            {
                var key=$"credit-delivery-{dispatch:N}";
                using var accepted=await admin.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/settle",new SettleDispatchRequest(0,null,key));
                Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
                var deadline=DateTimeOffset.UtcNow.AddSeconds(30);
                while(await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id",dispatch)!="Closed"&&DateTimeOffset.UtcNow<deadline)await Task.Delay(100);
                Assert.Equal("Closed",await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id",dispatch));
                Assert.Equal(11900,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPaymentApplications application JOIN dbo.Receivables position ON position.ReceivableId=application.ReceivableId WHERE position.SourceDocumentId=@Id",sale.DocumentId));
                Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",sale.DocumentId));
                await using var cash=new SqlCommand("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=@Key",connection);cash.Parameters.AddWithValue("@Key",$"dispatch-settlement:{key}");Assert.Equal(0,(int)(await cash.ExecuteScalarAsync())!);
            }
            finally{await worker.StopAsync(default);worker.Dispose();}
        }
        finally{await RestoreShortageAccountAsync(previous);}
    }
}
