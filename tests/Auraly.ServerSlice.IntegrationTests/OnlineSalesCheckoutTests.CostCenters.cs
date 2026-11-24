using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class OnlineSalesCheckoutTests
{
    [Fact]
    public async Task Device_cost_center_catalog_requires_the_actual_sales_permission_and_owned_session()
    {
        using var device=fixture.CreateClient();
        device.DefaultRequestHeaders.Add("X-Auraly-Device-Id",fixture.DeviceId.ToString("D"));
        device.DefaultRequestHeaders.Add("X-Auraly-Device-Secret",ServerSliceFixture.DeviceSecret);
        device.DefaultRequestHeaders.Add("X-Auraly-User-Id",fixture.UserId.ToString("D"));
        device.DefaultRequestHeaders.Add("X-Auraly-Business-Id",fixture.BusinessId.ToString("D"));
        device.DefaultRequestHeaders.Add("X-Auraly-Work-Session-Id",fixture.WorkSessionId.ToString("D"));
        var center=Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.AccountingCostCenters(CostCenterId,TenantId,Code,Name,IsDefault,IsActive,CreatedAt)
            VALUES(@Center,@Tenant,@Code,N'Catálogo de caja instalada',0,1,SYSUTCDATETIME());
            """,new SqlParameter("@Center",center),new SqlParameter("@Tenant",fixture.TenantId),new SqlParameter("@Code","DEVICE-"+center.ToString("N")[..12]));
        // This role initially has no SalesCreate grant in the fixture.
        await ExecuteAsync("""
            DELETE grantValue FROM dbo.RolePermissions grantValue JOIN dbo.Permissions permission
              ON permission.PermissionId=grantValue.PermissionId
            WHERE grantValue.RoleId=@Role AND permission.Resource=N'sales.create';
            """,new SqlParameter("@Role",fixture.RoleId));
        var path=$"/api/pos/v1/accounting/cost-centers?ids={center:D}&page=1&pageSize=1&isActive=true";
        using(var denied=await device.GetAsync(path))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await ExecuteAsync("""
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@Role,PermissionId,SYSUTCDATETIME() FROM dbo.Permissions WHERE Resource=N'sales.create';
            """,new SqlParameter("@Role",fixture.RoleId));
        using var counter=new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("SELECT COUNT(*) FROM dbo.AccountingCostCenters",StringComparison.Ordinal));
        using var response=await device.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var page=(await response.Content.ReadFromJsonAsync<AccountingCostCenterPage>())!;
        Assert.Equal(center,Assert.Single(page.Items).CostCenterId);Assert.Equal(1,counter.Count);
        device.DefaultRequestHeaders.Remove("X-Auraly-Work-Session-Id");
        device.DefaultRequestHeaders.Add("X-Auraly-Work-Session-Id",Guid.NewGuid().ToString("D"));
        using var wrongSession=await device.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,wrongSession.StatusCode);
        Assert.Equal(1,counter.Count);
    }

    [Theory]
    [InlineData(PosSaleDocumentTypes.Invoice,true)]
    [InlineData(PosSaleDocumentTypes.Invoice,false)]
    [InlineData(PosSaleDocumentTypes.Receipt,true)]
    [InlineData(PosSaleDocumentTypes.Receipt,false)]
    public async Task Invoice_cost_center_uses_selected_or_automatic_and_replay_preserves_one_balanced_entry(string documentType,bool explicitCenter)
    {
        var actor=await CreateUserAsync("invoice-center");
        using var client=fixture.CreateUserClient(actor,CommercePermissionCodes.SalesCreate);
        var center=Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.AccountingCostCenters(CostCenterId,TenantId,Code,Name,IsDefault,IsActive,CreatedAt)
            VALUES(@Center,@Tenant,@Code,N'Centro de la caja',0,1,SYSUTCDATETIME());
            """,new SqlParameter("@Center",center),new SqlParameter("@Tenant",fixture.TenantId),new SqlParameter("@Code","BOX-"+center.ToString("N")[..12]));
        var options=(await client.GetFromJsonAsync<AccountingCostCenterPage>(
            $"/api/commerce/v1/accounting/cost-centers?ids={center:D}&pageSize=1&isActive=true"))!;
        Assert.Equal(center,Assert.Single(options.Items).CostCenterId);
        using(var selected=await client.PostAsJsonAsync("/api/commerce/v1/pos/workspace/select",
            new SalesWorkspaceSelection(fixture.BusinessId,fixture.WarehouseId,explicitCenter?center:null)))
        {
            selected.EnsureSuccessStatusCode();
            Assert.Equal(explicitCenter?center:(Guid?)null,
                (await selected.Content.ReadFromJsonAsync<SalesWorkspaceContext>())!.CostCenterId);
        }
        var draft=await CaptureAsync(client,await OpenAsync(client));
        var command=new CompleteOnlineSalesDraftRequest(draft.Version,
            [new OnlineSalesPayment("Cash",draft.PayableAmount,null)],DocumentType:documentType,
            CostCenterId:explicitCenter?center:null);
        var key="box-center-"+Guid.NewGuid().ToString("N");
        using var acceptedCounter=new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:sql=>sql.CommandText.Contains("INSERT dbo.OnlineSalesCheckoutReceipts",StringComparison.Ordinal));
        var completed=await CompleteAsync(client,draft.DraftId,command,key);
        Assert.Equal(1,acceptedCounter.Count);
        // Changing the setup after issuance cannot retarget the original document or its replay.
        using(var selected=await client.PostAsJsonAsync("/api/commerce/v1/pos/workspace/select",
            new SalesWorkspaceSelection(fixture.BusinessId,fixture.WarehouseId,null))) selected.EnsureSuccessStatusCode();
        if(explicitCenter) await ExecuteAsync("UPDATE dbo.AccountingCostCenters SET IsActive=0,Name=N'Nombre posterior' WHERE CostCenterId=@Center",new SqlParameter("@Center",center));
        var replay=await CompleteAsync(client,draft.DraftId,command,key);
        Assert.Equal(completed.Receipt.DocumentId,replay.Receipt.DocumentId);
        Assert.Equal(1,acceptedCounter.Count);
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify=new SqlCommand("""
            SELECT document.CostCenterId,job.ResolvedCostCenterId,
              (SELECT COUNT(*) FROM dbo.AccountingEntries entry WHERE entry.SourceDocumentId=document.DocumentId),
              (SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId WHERE entry.SourceDocumentId=document.DocumentId),
              (SELECT COUNT(DISTINCT line.CostCenterId) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId WHERE entry.SourceDocumentId=document.DocumentId),
              (SELECT COUNT(*) FROM dbo.InventoryMovements movement WHERE movement.DocumentId=document.DocumentId),
              (SELECT MAX(DATEDIFF_BIG(millisecond,processing.StartedAt,processing.CompletedAt)) FROM dbo.DocumentProcessingJobs processing WHERE processing.DocumentId=document.DocumentId AND processing.Status=N'Completed')
            FROM dbo.SalesDocuments document JOIN dbo.AccountingPostingJobs job
              ON job.SourceDocumentId=document.DocumentId AND job.BusinessId=document.BusinessId AND job.SourceDocumentType=document.DocumentType
            WHERE document.DocumentId=@Document AND document.BusinessId=@Business;
            """,connection);
        verify.Parameters.AddWithValue("@Document",completed.Receipt.DocumentId);
        verify.Parameters.AddWithValue("@Business",fixture.BusinessId);
        await using var rows=await verify.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        Assert.Equal(explicitCenter?center:(Guid?)null,rows.IsDBNull(0)?null:rows.GetGuid(0));
        Assert.False(rows.IsDBNull(1));
        if(explicitCenter) Assert.Equal(center,rows.GetGuid(1));
        Assert.Equal(1,rows.GetInt32(2));Assert.Equal(0m,rows.GetDecimal(3));
        Assert.Equal(1,rows.GetInt32(4));Assert.Equal(1,rows.GetInt32(5));
        Assert.False(rows.IsDBNull(6));
        var elapsed=rows.GetInt64(6);
        Assert.InRange(elapsed,0,1999);
        Console.WriteLine($"CostCenter {documentType} explicit={explicitCenter}: motor={elapsed} ms; acceptance commands={acceptedCounter.Count}.");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("foreign")]
    public async Task Unavailable_cost_center_rejects_before_consuming_draft_or_accepting_source_and_can_retry(string unavailable)
    {
        var actor=await CreateUserAsync("invalid-center");
        using var client=fixture.CreateUserClient(actor,CommercePermissionCodes.SalesCreate);
        var center=Guid.NewGuid();
        if(unavailable=="inactive") await ExecuteAsync("""
            INSERT dbo.AccountingCostCenters(CostCenterId,TenantId,Code,Name,IsDefault,IsActive,CreatedAt)
            VALUES(@Center,@Tenant,@Code,N'Centro retirado',0,0,SYSUTCDATETIME());
            """,new SqlParameter("@Center",center),new SqlParameter("@Tenant",fixture.TenantId),new SqlParameter("@Code","OFF-"+center.ToString("N")[..12]));
        if(unavailable=="foreign") await ExecuteAsync("""
            INSERT dbo.Tenants(TenantId,Name,Email) VALUES(@Tenant,N'Empresa privada',@Email);
            INSERT dbo.AccountingCostCenters(CostCenterId,TenantId,Code,Name,IsDefault,IsActive,CreatedAt)
            VALUES(@Center,@Tenant,N'FOREIGN',N'Centro de otra empresa',1,1,SYSUTCDATETIME());
            """,new SqlParameter("@Center",center),new SqlParameter("@Tenant",Guid.NewGuid()),new SqlParameter("@Email",$"{center:N}@example.test"));
        var draft=await CaptureAsync(client,await OpenAsync(client));
        var command=new CompleteOnlineSalesDraftRequest(draft.Version,
            [new OnlineSalesPayment("Cash",draft.PayableAmount,null)],DocumentType:PosSaleDocumentTypes.Receipt,
            CostCenterId:center);
        var key=Guid.NewGuid().ToString("N");
        using(var request=Mutation(draft.DraftId,command,key))
        using(var response=await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            Assert.Contains("centro de costo",await response.Content.ReadAsStringAsync());
        }
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var check=new SqlCommand("SELECT COUNT(*) FROM dbo.OnlineSalesCheckoutReceipts WHERE SalesDraftId=@Draft",connection);
            check.Parameters.AddWithValue("@Draft",draft.DraftId);
            Assert.Equal(0,Convert.ToInt32(await check.ExecuteScalarAsync()));
        }
        var completed=await CompleteAsync(client,draft.DraftId,command with {CostCenterId=null},key);
        Assert.NotEqual(Guid.Empty,completed.Receipt.DocumentId);
    }
}
