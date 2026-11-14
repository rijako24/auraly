using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PaymentMethodCatalogTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task An_unused_form_type_can_be_corrected_before_a_cached_sale_is_accepted()
    {
        using var client = fixture.CreateAdminClient(CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,
            Auraly.Commerce.Accounting.Contracts.AccountingPermissionCodes.Read,
            Auraly.Commerce.Accounting.Contracts.AccountingPermissionCodes.Configure);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var credit = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1396","Asset","Receivable","Debit");
        var types = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var request = new SavePaymentMethodRequest(Guid.NewGuid(),$"UNUSED-{Guid.NewGuid():N}","Forma sin movimientos",
            types.Single(type => type.Code == "Cash").Id,cash.AccountId,true,false,false,false,true,10);
        using var created = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}",request);
        Assert.True(created.IsSuccessStatusCode,await created.Content.ReadAsStringAsync());
        var form = (await created.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        using var changed = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}",
            request with { PaymentTypeId = types.Single(type => type.Code == "Credit").Id,
                AccountId = credit.AccountId, RowVersion = form.RowVersion });
        Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
        var result = (await changed.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        Assert.Equal("Credit",result.MethodCode);
        Assert.Equal(2,result.Version);

        // A device may still hold the old type before the server has seen its
        // first sale. Reject before acceptance, so an operator can correct the
        // unused form and retry the same issued source instead of stranding it.
        var sale = fixture.CreateValidRequest(9_411);
        sale = sale with { Payments = [new Auraly.Contracts.Sales.PosSalePaymentContract(1,"Cash",sale.CommercialSnapshot.PayableAmount,null,
            PaymentMethodId:form.PaymentMethodId,PaymentMethodVersion:form.Version,
            PaymentMethodCode:form.Code,PaymentMethodName:form.Name,AccountId:form.AccountId)] };
        using var device = fixture.CreateClient();
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await device.SendAsync(upload))
        {
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            Assert.Contains("forma de pago de la venta",await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0,await fixture.CountAsync("SalesDocuments",sale.DocumentId));
        Assert.Equal(0,await fixture.CountAsync("DocumentProcessingJobs",sale.DocumentId));
        using var restored = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}",
            request with { RowVersion = result.RowVersion });
        Assert.True(restored.IsSuccessStatusCode,await restored.Content.ReadAsStringAsync());
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await device.SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        Assert.Equal(1,await fixture.CountAsync("SalesDocuments",sale.DocumentId));
    }

    [Theory]
    [InlineData("Cash", "General")]
    [InlineData("Transfer", "Bank")]
    public async Task A_form_cannot_use_an_incompatible_or_unbacked_treasury_account(string methodCode, string function)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var accountId = Guid.NewGuid();
        await using var command = new SqlCommand("""
            INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,
              IsActive,CreatedAt,FinancialFunction,BalanceSide)
            VALUES(@Id,@Tenant,@Code,N'Auxiliar sin capacidad de recaudo',N'Asset',1,0,1,SYSUTCDATETIME(),@Function,N'Debit');
            SELECT OptionId FROM reference.Options WHERE CatalogCode=N'payment-method' AND Code=@Method;
            """, connection);
        command.Parameters.AddWithValue("@Id", accountId);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Code", $"INVALID-{Guid.NewGuid():N}"[..30]);
        command.Parameters.AddWithValue("@Function", function);
        command.Parameters.AddWithValue("@Method", methodCode);
        var typeId = (Guid)(await command.ExecuteScalarAsync())!;
        using var client = fixture.CreateAdminClient(CatalogPermissionCodes.Update);
        var request = new SavePaymentMethodRequest(Guid.NewGuid(), $"INVALID-{Guid.NewGuid():N}",
            "Forma inválida", typeId, accountId, false, true, false, false, true, 10);

        using var response = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_pages_and_concurrent_mutations_share_the_tenant_owner()
    {
        var accountId=Guid.NewGuid();
        var typeId=Guid.Empty;
        var code=$"PM-{Guid.NewGuid():N}"[..30];
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command=new SqlCommand("""
                INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,
                  IsActive,CreatedAt,FinancialFunction,BalanceSide)
                VALUES(@Id,@Tenant,@Code,N'Caja de prueba de formas',N'Asset',1,0,1,SYSUTCDATETIME(),N'Cash',N'Debit');
                SELECT OptionId FROM reference.Options WHERE CatalogCode=N'payment-method' AND Code=N'Cash';
                """,connection);
            command.Parameters.AddWithValue("@Id",accountId);command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            command.Parameters.AddWithValue("@Code",code);
            typeId=(Guid)(await command.ExecuteScalarAsync())!;
        }
        using var client=fixture.CreateAdminClient(CatalogPermissionCodes.Read,CatalogPermissionCodes.Update);
        var request=new SavePaymentMethodRequest(Guid.NewGuid(),code,"Caja configurada",typeId,accountId,
            true,true,true,true,true,10);
        using var created=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}?page=1&pageSize=1&search={code}",request);
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var first=(await created.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!;
        Assert.Single(first.Page.Items);Assert.Equal(1,first.Page.TotalCount);
        Assert.Equal(accountId,first.Method.AccountId);
        var changed=request with {Name="Caja renombrada",RowVersion=first.Method.RowVersion};
        using var updated=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}?page=1&pageSize=1",changed);
        Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
        var second=(await updated.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!;
        Assert.Equal(2,second.Method.Version);
        using var stale=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}?page=1&pageSize=1",changed);
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        // Invalid page input must not mutate the head before returning an error.
        using var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{request.PaymentMethodId}?page=0&pageSize=1",
            changed with {Name="No debe guardarse",RowVersion=second.Method.RowVersion});
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        await using var verification=new SqlConnection(fixture.ConnectionString);await verification.OpenAsync();
        await using var verify=new SqlCommand("""
            SELECT Name,Version FROM reference.PaymentMethods WHERE TenantId=@Tenant AND PaymentMethodId=@Id;
            """,verification);
        verify.Parameters.AddWithValue("@Tenant",fixture.TenantId);verify.Parameters.AddWithValue("@Id",request.PaymentMethodId);
        await using var reader=await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Caja renombrada",reader.GetString(0));Assert.Equal(2,reader.GetInt32(1));
    }
}
