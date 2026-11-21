using System.Net.Http.Json;
using Auraly.Application.Dispatching;
using Auraly.Application.Returns;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(false,.25,.75,8925)]
    [InlineData(true,.25,.75,8925)]
    [InlineData(false,.333333,.666667,7933.3373)]
    [InlineData(false,.25,.75,8925,true)]
    [InlineData(true,.25,.75,8925,true)]
    public async Task Dispatch_assignment_uses_only_the_remaining_sale_after_a_prior_return(bool credit,decimal priorQuantity,decimal remainingQuantity,decimal remainingAmount,bool raceAssignment=false)
    {
        using var admin=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,AccountingPermissionCodes.Activate);
        using(var defaults=await admin.PutAsync("/api/commerce/v1/accounting/defaults",null))defaults.EnsureSuccessStatusCode();
        using(var activation=await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))activation.EnsureSuccessStatusCode();
        var(customer,site)=await CreateCustomerAsync();
        var source=fixture.CreateUniqueValidRequest();
        var request=WithUblSnapshot(source with{
            CustomerId=customer,CustomerPartySiteId=site,
            Credit=credit?new PosSaleCreditContract(customer,11900,new(2026,8,31,0,0,0,TimeSpan.FromHours(-5)),PartySiteId:site):null,
            Payments=credit?[]:source.Payments});
        if(credit)request=request with{UblSnapshot=request.UblSnapshot! with{PaymentFormCode="2",DueDate=new(2026,8,31)}};
        using(var pos=fixture.CreateClient())using(var message=fixture.CreateUploadMessage(request))using(var response=await pos.SendAsync(message))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await AssertBalancedAsync(request.DocumentId);
        using var scope=fixture.CreateScope();
        var returns=scope.ServiceProvider.GetRequiredService<SalesReturnService>();
        var reason=await ScalarAsync<string>("SELECT TOP(1) Code FROM dbo.BusinessReasons WHERE BusinessId=@Id AND ReasonType=N'SalesReturn' AND IsActive=1 ORDER BY DisplayOrder,Code",fixture.BusinessId);
        var returnUser=new SalesReturnUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{SalesReturnPermissionCodes.Create});
        Guid? refundSession=credit?null:await fixture.OpenWebWorkSessionAsync();
        var priorRequest=new ConfirmSalesReturnRequest(
            Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,request.DocumentId,DateTimeOffset.UtcNow,
            credit?ReturnEconomicResolutions.CustomerCredit:ReturnEconomicResolutions.Refund,credit?null:"Cash","Devolución anterior al despacho",
            [new(1,priorQuantity,"Sellable")],WorkSessionId:refundSession,ReasonCode:reason);
        var actor=new DispatchActorIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{
            DispatchPermissionCodes.Create,DispatchPermissionCodes.Read,DispatchPermissionCodes.ReadAll,DispatchPermissionCodes.AttachDocuments},true);
        var store=scope.ServiceProvider.GetRequiredService<IDispatchStore>();
        var logistics=scope.ServiceProvider.GetRequiredService<DispatchService>();
        Task<SalesReturnAcceptance> ReturnPriorAsync()=>returns.ConfirmAsync(returnUser,$"prior-return:{request.DocumentId:N}",priorRequest);
        var carrierId=await fixture.SeedDispatchCarrierAsync(fixture.UserId);
        if(raceAssignment)
        {
            SalesReturnAcceptance? acceptedReturn=null;
            async Task TryReturnAsync()
            {
                try { acceptedReturn=await ReturnPriorAsync(); }
                catch(SalesReturnConflictException) { }
            }
            var returning=TryReturnAsync();
            var assigning=logistics.CreateAsync(actor,new(fixture.BusinessId,new(2026,11,18),"Asignación concurrente",null,carrierId,null,[request.DocumentId]),default);
            await Task.WhenAll(returning,assigning);
            var assigned=await assigning;
            var racedDetail=(await store.GetAsync(actor,assigned.DispatchId,default))!;
            Assert.Equal(acceptedReturn is null?1m:remainingQuantity,assigned.ExpectedQuantity);
            Assert.Equal(acceptedReturn is null?11900m:remainingAmount,Assert.Single(racedDetail.Documents).DocumentTotal);
            if(acceptedReturn is not null)await AssertBalancedAsync(acceptedReturn.ReturnId);
            Assert.Equal(acceptedReturn is null?0:1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesReturns WHERE OriginalDocumentId=@Id",request.DocumentId));
            Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",request.DocumentId));
            return;
        }
        var returned=await ReturnPriorAsync();
        await AssertBalancedAsync(returned.ReturnId);
        var candidates=await store.CandidatesAsync(actor,new(1,10,request.DocumentNumber.FullNumber),default);
        var candidate=Assert.Single(candidates.Items);
        Assert.Equal(remainingQuantity,candidate.PendingQuantity);
        Assert.Equal(remainingAmount,candidate.DocumentTotal);
        var draft=await logistics.CreateAsync(actor,new(fixture.BusinessId,new(2026,11,18),"Transportador",null,carrierId,null,[request.DocumentId]),default);
        Assert.Equal(remainingQuantity,draft.ExpectedQuantity);
        var detail=await store.GetAsync(actor,draft.DispatchId,default);
        Assert.Equal(remainingAmount,Assert.Single(detail!.Documents).DocumentTotal);
        Assert.Equal(remainingQuantity,Assert.Single(detail.Lines).AssignedQuantity);
        var execution=await scope.ServiceProvider.GetRequiredService<IDispatchDeliveryStore>().GetAsync(actor,draft.DispatchId,default);
        var document=Assert.Single(execution!.Documents);
        Assert.Equal(credit?remainingAmount:0m,document.CreditAmount);
        await Assert.ThrowsAsync<SalesReturnConflictException>(()=>returns.ConfirmAsync(returnUser,$"uncoordinated:{draft.DispatchId:N}",new ConfirmSalesReturnRequest(
            Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,request.DocumentId,DateTimeOffset.UtcNow,
            credit?ReturnEconomicResolutions.CustomerCredit:ReturnEconomicResolutions.Refund,credit?null:"Cash","Devolución sin entrega",
            [new(1,.25m,"Sellable")],WorkSessionId:refundSession,ReasonCode:reason)));
        Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesReturns WHERE OriginalDocumentId=@Id",request.DocumentId));
        if(credit)
        {
            var invented=new ConfirmSalesReturnRequest(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,request.DocumentId,
                DateTimeOffset.UtcNow,ReturnEconomicResolutions.CustomerCredit,null,"No hay una liquidación aceptada",
                [new(1,.25m,"Sellable")],ReasonCode:reason,DispatchId:draft.DispatchId);
            await Assert.ThrowsAsync<SalesReturnForbiddenException>(()=>returns.ConfirmAsync(returnUser,Guid.NewGuid().ToString("N"),invented));
            await Assert.ThrowsAsync<SalesReturnConflictException>(()=>returns.ConfirmAsync(returnUser with{
                Permissions=new HashSet<string>{SalesReturnPermissionCodes.Create,DispatchPermissionCodes.Settle}},Guid.NewGuid().ToString("N"),invented));
        }
        await logistics.RemoveDocumentAsync(actor,draft.DispatchId,request.DocumentId,draft.RowVersion,default);
        var finalReturn=await returns.ConfirmAsync(returnUser,$"final-return:{draft.DispatchId:N}",new ConfirmSalesReturnRequest(
            Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,request.DocumentId,DateTimeOffset.UtcNow,
            credit?ReturnEconomicResolutions.CustomerCredit:ReturnEconomicResolutions.Refund,credit?null:"Cash","Última fracción disponible",
            [new(1,remainingQuantity,"Sellable")],WorkSessionId:refundSession,ReasonCode:reason));
        await AssertBalancedAsync(finalReturn.ReturnId);
        Assert.Equal(11900m,await ScalarAsync<decimal>("SELECT SUM(TotalAmount) FROM dbo.SalesReturns WHERE OriginalDocumentId=@Id",request.DocumentId));
        Assert.Empty((await store.CandidatesAsync(actor,new(1,10,request.DocumentNumber.FullNumber),default)).Items);
        await Assert.ThrowsAsync<DispatchConflictException>(()=>logistics.CreateAsync(actor,new(fixture.BusinessId,new(2026,11,18),"Sin mercancía",null,carrierId,null,[request.DocumentId]),default));
    }
}
