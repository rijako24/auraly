using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Sales;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Receivables;
using Auraly.Application.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(null, 0, false)]
    [InlineData(null, 1900, false)]
    [InlineData("Cash", 0, false)]
    [InlineData("Cash", 1900, false)]
    [InlineData("Transfer", 0, false)]
    [InlineData("Transfer", 1900, false)]
    [InlineData("DebitCard", 0, false)]
    [InlineData("CreditCard", 1900, false)]
    [InlineData("Cash", 1900, true)]
    [InlineData("DebitCard", 1900, true)]
    [InlineData(null, 0, false, true)]
    [InlineData(null, 1900, false, true)]
    public async Task Credit_delivery_keeps_or_collects_the_current_balance_once(string? method, decimal priorPayment, bool convergeForms, bool keepCurrent=false)
    {
        using var admin=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,DispatchPermissionCodes.Settle,CatalogPermissionCodes.Update,"sales.payments.correct");
        using(var defaults=await admin.PutAsync("/api/commerce/v1/accounting/defaults",null))defaults.EnsureSuccessStatusCode();
        using(var activation=await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))activation.EnsureSuccessStatusCode();
        var(customer,site)=await CreateCustomerAsync();
        var sale=WithUblSnapshot(fixture.CreateUniqueValidRequest() with{CustomerId=customer,CustomerPartySiteId=site,Payments=[],
            Credit=new PosSaleCreditContract(customer,11900,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.FromHours(-5)),PartySiteId:site)});
        sale=sale with{UblSnapshot=sale.UblSnapshot! with{PaymentFormCode="2",DueDate=new(2026,8,31)}};
        using(var upload=fixture.CreateUploadMessage(sale))using(var response=await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await AssertBalancedAsync(sale.DocumentId);
        var remaining=11900-priorPayment;
        var form=method is null?null:await CreateClosureFormAsync(admin,method);
        var secondForm=convergeForms?await CreateClosureFormAsync(admin,method!):null;
        using var scope=fixture.CreateScope();
        if(priorPayment>0)
        {
            await using var priorConnection=new SqlConnection(fixture.ConnectionString);await priorConnection.OpenAsync();
            await fixture.EnsurePortfolioPaymentSeriesAsync(priorConnection,"ReceivablePayment","RCC");
            var debt=await ScalarAsync<Guid>("SELECT ReceivableId FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId);
            await scope.ServiceProvider.GetRequiredService<ReceivablesService>().ConfirmPaymentAsync(
                new(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{ReceivablesPermissionCodes.RegisterPayment}),
                Guid.NewGuid().ToString("N"),new(Guid.NewGuid(),fixture.BusinessId,customer,null,
                    new DateTimeOffset(2026,8,1,12,0,0,TimeSpan.Zero),"COP","Abono anterior al despacho",[new(debt,priorPayment)],[new("Cash",priorPayment)]));
        }
        var dispatch=Guid.NewGuid();var source=Guid.NewGuid();var settlement=Guid.NewGuid();
        var previous=await SeedDispatchSettlementAsync(dispatch,source,settlement,sale.DocumentId,11900);
        try
        {
            await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
            await ResetSeededDispatchForDeliveryAsync(connection,dispatch);
            using var driver=fixture.CreateUserClient(fixture.UserId,DispatchPermissionCodes.ExecuteDeliveries);
            var signed=new DispatchDeliveryPaymentInput("CreditDocument",null,0,null,"https://qa.invalid/factura-firmada.png");
            var request=new RecordDispatchDeliveryRequest(source,"Delivered",null,null,null,null,
                new DateTimeOffset(2026,11,17,16,0,0,TimeSpan.Zero),Guid.NewGuid().ToString("N"),[signed],[]);
            var url=$"/api/commerce/v1/dispatches/{dispatch}/delivery-results";
            foreach(var application in new[]{"CreditAdvance","InvoicePayment"})
            {
                using var attempted=await driver.PutAsJsonAsync(url,request with{Payments=[signed,new(application,"Cash",1000,null,null)]});
                Assert.Equal(HttpStatusCode.BadRequest,attempted.StatusCode);Assert.Contains("saldo disponible actual",await attempted.Content.ReadAsStringAsync());
            }
            using(var notDelivered=await driver.PutAsJsonAsync(url,request with{DeliveryStatus="NotDelivered",Reason="Cliente ausente",Payments=[],IdempotencyKey=Guid.NewGuid().ToString("N")}))
                Assert.True(notDelivered.IsSuccessStatusCode,await notDelivered.Content.ReadAsStringAsync());
            Assert.Equal(remaining,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId));
            if(keepCurrent)request=request with{Payments=[signed,new("KeepCurrent",null,0,null,null)]};
            if(form is not null)
            {
                var card=method is "DebitCard" or "CreditCard";
                var franchise=card?(await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code:null;
                request=request with {Payments=[signed,new("CreditAdvance",method,convergeForms?remaining/2:remaining,method=="Transfer"?"TRF-CREDITO":null,null,
                    form.PaymentMethodId,form.Version,franchise,card?"APR-CREDITO":null)]};
                if(secondForm is not null)request=request with{Payments=[..request.Payments,new("CreditAdvance",method,remaining/2,null,null,secondForm.PaymentMethodId,secondForm.Version,franchise,card?"APR-SECOND":null)]};
            }
            using(var delivered=await driver.PutAsJsonAsync(url,request))
            {
                Assert.True(delivered.IsSuccessStatusCode,await delivered.Content.ReadAsStringAsync());
                var execution=(await delivered.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!;
                var payments=Assert.Single(execution.Documents).Payments;
                var payment=Assert.Single(payments,item=>item.ApplicationType=="CreditDocument");Assert.Null(payment.PaymentMethod);Assert.Equal(0,payment.Amount);
                Assert.Equal(form is null?keepCurrent?2:1:convergeForms?3:2,payments.Count);
            }
            var cash=method=="Cash"?remaining:0;
            using(var closed=await driver.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/close-route",new CloseDispatchRouteRequest(cash,null,Guid.NewGuid().ToString("N"),
                method is "DebitCard" or "CreditCard"?remaining:0,method=="Transfer"?remaining:0)))
                Assert.True(closed.IsSuccessStatusCode,await closed.Content.ReadAsStringAsync());
            if(secondForm is not null)
            {
                await using var lookup=new SqlCommand("SELECT DispatchDeliveryPaymentId FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND PaymentMethodId=@Form",connection);
                lookup.Parameters.AddWithValue("@Id",dispatch);lookup.Parameters.AddWithValue("@Form",secondForm.PaymentMethodId);
                var paymentId=(Guid)(await lookup.ExecuteScalarAsync())!;
                var correction=new CorrectDispatchPaymentRequest(form!.PaymentMethodId,form.Version,method!,null,
                    method=="DebitCard"?request.Payments.First(value=>value.ApplicationType=="CreditAdvance").CardFranchiseCode:null,method=="DebitCard"?"APR-CREDITO":null,"Misma forma real para ambos registros");
                if(method=="DebitCard")
                {
                    using var ambiguous=await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{paymentId}/correction",correction with{ApprovalNumber="APR-SECOND"});
                    Assert.Equal(HttpStatusCode.Conflict,ambiguous.StatusCode);
                    Assert.Contains("otro soporte",await ambiguous.Content.ReadAsStringAsync());
                }
                using var corrected=await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{paymentId}/correction",correction);
                Assert.True(corrected.IsSuccessStatusCode,await corrected.Content.ReadAsStringAsync());
            }
            if(form is not null&&method!="Cash")
            {
                var payments=await driver.GetFromJsonAsync<DispatchExecutionDetail>($"/api/commerce/v1/dispatches/{dispatch}/execution");
                foreach(var recorded in Assert.Single(payments!.Documents).Payments.Where(value=>value.ApplicationType=="CreditAdvance"))
                {
                    using var verified=await admin.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{recorded.PaymentId}/verification",new VerifyDispatchPaymentRequest(true,recorded.CorrectedAt));
                    Assert.True(verified.IsSuccessStatusCode,await verified.Content.ReadAsStringAsync());
                }
            }
            var worker=ActivatorUtilities.CreateInstance<DispatchSettlementHostedService>(fixture.Services);await worker.StartAsync(default);
            try
            {
                var key=$"credit-delivery-{dispatch:N}";
                using var accepted=await admin.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/settle",new SettleDispatchRequest(cash,null,key));
                Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
                var deadline=DateTimeOffset.UtcNow.AddSeconds(30);
                while(await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id",dispatch)!="Closed"&&DateTimeOffset.UtcNow<deadline)await Task.Delay(100);
                Assert.Equal("Closed",await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id",dispatch));
                Assert.Equal(form is null?remaining:0,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId));
                Assert.Equal((form is null?0:1)+(priorPayment>0?1:0),await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPaymentApplications application JOIN dbo.Receivables position ON position.ReceivableId=application.ReceivableId WHERE position.SourceDocumentId=@Id",sale.DocumentId));
                Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",sale.DocumentId));
                await using var money=new SqlCommand("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=@Key",connection);money.Parameters.AddWithValue("@Key",$"dispatch-settlement:{key}");Assert.Equal(0,(int)(await money.ExecuteScalarAsync())!);
                using var replay=await admin.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/settle",new SettleDispatchRequest(cash,null,key));
                Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
                Assert.Equal(form is null?remaining:0,await ScalarAsync<decimal>("SELECT CreditDocumentTotal FROM dispatch.Closures WHERE DispatchId=@Id",dispatch));
                Assert.Equal(priorPayment,await ScalarAsync<decimal>("SELECT PreviouslyCollectedTotal FROM dispatch.PaymentTotals(@Id)",dispatch));
                if(form is not null)
                {
                    await using var rename=new SqlCommand("UPDATE reference.PaymentMethods SET Name=N'Configuración posterior' WHERE PaymentMethodId=@Id",connection);
                    rename.Parameters.AddWithValue("@Id",form.PaymentMethodId);await rename.ExecuteNonQueryAsync();
                }
                await AssertCreditHistoryAsync(sale,method??"Credit",form is null?11900:remaining,form?.Name,form is null?0:priorPayment,convergeForms?2:1);
            }
            finally{await worker.StopAsync(default);worker.Dispose();}
        }
        finally{await RestoreShortageAccountAsync(previous);}
    }

    private static async Task ResetSeededDispatchForDeliveryAsync(SqlConnection connection,Guid dispatch)
    {
        await using var setup=new SqlCommand("""
            DELETE total FROM dbo.WorkSessionClosurePaymentTotals total JOIN dispatch.Closures closure ON closure.DispatchSettlementId=total.WorkSessionClosureId WHERE closure.DispatchId=@Dispatch;
            DELETE closure FROM dbo.WorkSessionClosures closure JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId WHERE session.DispatchId=@Dispatch;
            DELETE dbo.WorkSessions WHERE DispatchId=@Dispatch;
            DELETE dbo.DispatchDeliveryPayments WHERE DispatchId=@Dispatch;
            UPDATE dbo.Dispatches SET Status=N'InDelivery' WHERE DispatchId=@Dispatch;
            """,connection);
        setup.Parameters.AddWithValue("@Dispatch",dispatch);await setup.ExecuteNonQueryAsync();
    }
}
