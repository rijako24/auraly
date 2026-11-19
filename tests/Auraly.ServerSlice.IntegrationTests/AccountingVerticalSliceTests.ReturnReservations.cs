using System.Net;
using System.Net.Http.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Returns;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Fact]
    public async Task Accepted_return_reserves_its_receivable_until_the_same_financial_effect_applies_once()
    {
        using var admin=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,ReceivablesPermissionCodes.RegisterPayment,CatalogPermissionCodes.Update);
        using(var defaults=await admin.PutAsync("/api/commerce/v1/accounting/defaults",null))defaults.EnsureSuccessStatusCode();
        using(var activate=await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))activate.EnsureSuccessStatusCode();
        var cash=await CreateClosureFormAsync(admin,"Cash");
        await using(var numbering=new SqlConnection(fixture.ConnectionString))
        {
            await numbering.OpenAsync();
            await fixture.EnsurePortfolioPaymentSeriesAsync(numbering,"ReceivablePayment","RCC");
        }
        var(customer,site)=await CreateCustomerAsync();
        var sale=WithUblSnapshot(fixture.CreateValidRequest(9958) with{CustomerId=customer,CustomerPartySiteId=site,Payments=[],
            Credit=new PosSaleCreditContract(customer,11900,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.FromHours(-5)),PartySiteId:site)});
        sale=sale with{UblSnapshot=sale.UblSnapshot! with{PaymentFormCode="2",DueDate=new(2026,8,31)}};
        using(var pos=fixture.CreateClient())using(var upload=fixture.CreateUploadMessage(sale))using(var response=await pos.SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await AssertBalancedAsync(sale.DocumentId);
        var receivable=await ScalarAsync<Guid>("SELECT ReceivableId FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale.DocumentId);
        var reason=await ScalarAsync<string>("SELECT TOP(1) Code FROM dbo.BusinessReasons WHERE BusinessId=@Id AND ReasonType=N'SalesReturn' AND IsActive=1 ORDER BY DisplayOrder,Code",fixture.BusinessId);
        using var scope=fixture.CreateScope();
        var accepted=await scope.ServiceProvider.GetRequiredService<ISalesReturnStore>().AcceptAsync(
            new(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{SalesReturnPermissionCodes.Create}),
            Guid.NewGuid().ToString("N"),new(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,sale.DocumentId,
                new DateTimeOffset(2026,11,18,12,0,0,TimeSpan.Zero),ReturnEconomicResolutions.CustomerCredit,null,
                "Devolución reservada",[new(1,.5m,ReturnInventoryDispositions.Sellable)],ReasonCode:reason),default);
        try
        {
            Assert.Equal(5950m,await ScalarAsync<decimal>("SELECT balance.AvailableAmount FROM dbo.Receivables debt CROSS APPLY accounting.ObligationAvailability(debt.BusinessId,N'Receivable',debt.ReceivableId,debt.OutstandingAmount,NULL) balance WHERE debt.ReceivableId=@Id",receivable));
            Assert.Equal(11900m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",receivable));
            var attemptedPayment=Guid.NewGuid();
            using var request=new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/receivable-payments/confirm"){
                Content=JsonContent.Create(new ConfirmCustomerPaymentRequest(attemptedPayment,fixture.BusinessId,customer,null,
                    new DateTimeOffset(2026,11,18,12,0,0,TimeSpan.Zero),"COP",null,[new(receivable,11900)],
                    [new("Cash",11900,PaymentMethodId:cash.PaymentMethodId,PaymentMethodVersion:cash.Version)]))};
            request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
            using var response=await admin.SendAsync(request);
            Assert.True(response.StatusCode==HttpStatusCode.Conflict,await response.Content.ReadAsStringAsync());
            Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPayments WHERE PaymentId=@Id",attemptedPayment));
        }
        finally
        {
            await scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>().ProcessOneAsync(
                new(accepted.MovementId,fixture.BusinessId,accepted.ReturnId,SalesReturnDocumentTypes.SalesReturn),default);
        }
        await AssertBalancedAsync(accepted.ReturnId);
        await scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>().ProcessOneAsync(
            new(accepted.MovementId,fixture.BusinessId,accepted.ReturnId,SalesReturnDocumentTypes.SalesReturn),default);
        Assert.Equal(5950m,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id",receivable));
        Assert.Equal(5950m,await ScalarAsync<decimal>("SELECT balance.AvailableAmount FROM dbo.Receivables debt CROSS APPLY accounting.ObligationAvailability(debt.BusinessId,N'Receivable',debt.ReceivableId,debt.OutstandingAmount,NULL) balance WHERE debt.ReceivableId=@Id",receivable));
        Assert.Equal(0,await CountAsync("CustomerCredits","SourceReturnId",accepted.ReturnId));
        Assert.Equal(1,await CountAsync("SalesReturnReceivableApplications","ReturnId",accepted.ReturnId));
        Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",accepted.ReturnId));
        Assert.Equal(1,await CountAsync("InventoryMovements","DocumentId",accepted.ReturnId));
    }
}
