using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Sales;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ReceivablesVerticalSliceTests
{
    [Theory]
    [InlineData(null, 3, true)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, false)]
    public async Task New_credit_counts_positive_overdue_invoices_but_not_paid_or_due_today(int? maximum, int overdue, bool allowed)
    {
        var (customerId,userId,siteId)=await ConfigureAsync();
        using var client=fixture.CreateUserClient(userId,ReceivablesPermissionCodes.Read,
            ReceivablesPermissionCodes.ManageCredit,ReceivablesPermissionCodes.RegisterPayment);
        using(var profile=await client.PutAsJsonAsync($"/api/commerce/v1/customers/{customerId}/credit",
            new UpdateCustomerCreditProfileRequest(fixture.BusinessId,500_000,30,true) {MaximumOverdueInvoices=maximum}))
            Assert.True(profile.IsSuccessStatusCode,await profile.Content.ReadAsStringAsync());
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await ScalarAsync<string>(
            "SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@Id",fixture.BusinessId));
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).DateTime);
        var dueToday=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(new TimeOnly(12,0)),zone));
        var issued=dueToday.AddDays(-30);
        var counterpart=await AccountIdByCodeAsync("413595");
        var rows=new List<PreexistingReceivableItemRequest>();
        for(var index=0;index<overdue;index++)
        {
            var id=Guid.NewGuid();
            rows.Add(new(id,customerId,null,siteId,$"OVERDUE-{id:N}",issued,issued.AddDays(1),100,counterpart,null));
        }
        var paidId=Guid.NewGuid();
        rows.Add(new(paidId,customerId,null,siteId,$"PAID-{paidId:N}",issued,issued.AddDays(1),100,counterpart,null));
        var todayId=Guid.NewGuid();
        rows.Add(new(todayId,customerId,null,siteId,$"TODAY-{todayId:N}",issued,dueToday,100,counterpart,null));
        using(var imported=await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import",
            new ImportPreexistingReceivablesRequest(fixture.BusinessId,rows)))
            Assert.True(imported.IsSuccessStatusCode,await imported.Content.ReadAsStringAsync());
        var allocations=new List<CustomerPaymentAllocationRequest> {new(paidId,100)};
        if(overdue>0) allocations.Add(new(rows[0].ReceivableId,1));
        var amount=allocations.Sum(value=>value.Amount);
        var payment=new ConfirmCustomerPaymentRequest(Guid.NewGuid(),fixture.BusinessId,customerId,null,dueToday,"COP",null,
            allocations,[new("Cash",amount,amount)]);
        using(var paid=await SendAsync(client,"/api/commerce/v1/receivable-payments/confirm",payment,payment.PaymentId.ToString("N")))
            Assert.True(paid.IsSuccessStatusCode,await paid.Content.ReadAsStringAsync());

        using var device=fixture.CreateClient();
        device.DefaultRequestHeaders.Add("X-Auraly-Device-Id",fixture.DeviceId.ToString("D"));
        device.DefaultRequestHeaders.Add("X-Auraly-Device-Secret",ServerSliceFixture.DeviceSecret);
        using var response=await device.PostAsJsonAsync("/api/pos/v1/sales/credit-validation",
            new PosCreditValidationRequest(fixture.BusinessId,customerId,10,FiscalEnvironment:2,PartySiteId:siteId));

        Assert.Equal(allowed?HttpStatusCode.OK:HttpStatusCode.Conflict,response.StatusCode);
        if(allowed)
        {
            var decision=(await response.Content.ReadFromJsonAsync<PosCreditValidationResult>())!;
            Assert.Equal(overdue,decision.OverdueInvoiceCount);
            Assert.Equal(maximum,decision.MaximumOverdueInvoices);
        }
        else
            Assert.Contains($"máximo permitido es {maximum}",await response.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Older_credit_editors_preserve_the_limit_and_explicit_null_clears_it()
    {
        var (customerId,userId,_)=await ConfigureAsync();
        using var client=fixture.CreateUserClient(userId,ReceivablesPermissionCodes.Read,ReceivablesPermissionCodes.ManageCredit);
        var url=$"/api/commerce/v1/customers/{customerId}/credit";
        using(var saved=await client.PutAsJsonAsync(url,new UpdateCustomerCreditProfileRequest(fixture.BusinessId,1000,30,true)
            {MaximumOverdueInvoices=2}))
            Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        using(var oldEditor=await client.PutAsJsonAsync(url,new {businessId=fixture.BusinessId,creditLimit=2000,defaultDueDays=30,isCreditEnabled=true}))
        {
            Assert.True(oldEditor.IsSuccessStatusCode,await oldEditor.Content.ReadAsStringAsync());
            var current=(await oldEditor.Content.ReadFromJsonAsync<CustomerCreditProfile>())!;
            Assert.Equal(2,current.MaximumOverdueInvoices);Assert.Equal(2000m,current.CreditLimit);
        }
        using(var invalid=await client.PutAsJsonAsync(url,new UpdateCustomerCreditProfileRequest(fixture.BusinessId,2000,30,true)
            {MaximumOverdueInvoices=-1}))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        Assert.Equal(2,(await client.GetFromJsonAsync<CustomerCreditProfile>(url))!.MaximumOverdueInvoices);
        using(var cleared=await client.PutAsJsonAsync(url,new {businessId=fixture.BusinessId,creditLimit=2000,defaultDueDays=30,isCreditEnabled=true,maximumOverdueInvoices=(int?)null}))
        {
            Assert.True(cleared.IsSuccessStatusCode,await cleared.Content.ReadAsStringAsync());
            Assert.Null((await cleared.Content.ReadFromJsonAsync<CustomerCreditProfile>())!.MaximumOverdueInvoices);
        }
    }
}
