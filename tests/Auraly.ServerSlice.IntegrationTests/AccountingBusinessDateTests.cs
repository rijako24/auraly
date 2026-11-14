using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Application.Catalog;
using Auraly.BuildingBlocks.Domain.Time;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingBusinessDateTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData("America/Bogota", "2026-11-11")]
    [InlineData("Asia/Tokyo", "2026-11-11")]
    [InlineData("America/New_York", "2026-03-08")]
    [InlineData("America/New_York", "2026-11-01")]
    [InlineData("America/Sao_Paulo", "2018-11-04")]
    [InlineData("Pacific/Apia", "2011-12-30")]
    public async Task Traceability_and_contextual_catalog_share_business_days_without_changing_the_legacy_contract(
        string zoneId, string text)
    {
        var day=DateOnly.Parse(text);var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var start=BusinessCalendar.StartOfDay(day,zone);var end=BusinessCalendar.StartOfDay(day.AddDays(1),zone);
        var utcDay=new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc));
        DateTimeOffset[] instants=[start.AddTicks(-1),start,end.AddTicks(-1),end,utcDay.AddHours(1),utcDay.AddHours(23),start.AddHours(2)];
        var rows=instants.Select((at,index)=>new {
            id=Guid.NewGuid(),jobId=Guid.NewGuid(),type=index==6?"RestaurantTip":"ManualAccountingVoucher",
            at=at.ToOffset(index==4?TimeSpan.FromHours(14):TimeSpan.Zero)
        }).ToArray();
        var business=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var seed=new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,TimeZone)
            VALUES(@Business,@Tenant,N'Calendario contable',N'',N'',N'',N'',N'',@Zone);
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@Actor,@Role,@Business,SYSUTCDATETIME());
            SELECT id,jobId,type,at INTO #Rows FROM OPENJSON(@Rows)
            WITH(id uniqueidentifier,jobId uniqueidentifier,type nvarchar(64),at datetimeoffset(7));
            DECLARE @Party uniqueidentifier=(SELECT PartyId FROM dbo.Suppliers WHERE SupplierId=@Supplier AND TenantId=@Tenant);
            DECLARE @Payload nvarchar(max)=JSON_MODIFY(N'{"PayloadVersion":4,"VoucherTypeCode":"Accounting","Lines":[]}',
                '$.MainPartyId',CONVERT(nvarchar(36),@Party));
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
            SELECT id,type,@Tenant,@Business,@Payload,HASHBYTES('SHA2_256',@Payload),at,at FROM #Rows;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
            SELECT jobId,@Tenant,@Business,id,type,HASHBYTES('SHA2_256',@Payload),at,N'Pending',0,at FROM #Rows;
            UPDATE source SET AccountingPostingJobId=rows.jobId
            FROM dbo.AccountingSourceDocuments source JOIN #Rows rows ON rows.id=source.SourceDocumentId AND rows.type=source.SourceDocumentType;
            INSERT accounting.SourcePartyIndex(PartyId,SourceDocumentId,SourceDocumentType)
            SELECT party.PartyId,rows.id,rows.type FROM #Rows rows
            CROSS APPLY accounting.ManualVoucherPartyReferences(@Payload) party
            WHERE rows.type=N'ManualAccountingVoucher';
            SELECT PartyId FROM dbo.Suppliers WHERE SupplierId=@Supplier AND TenantId=@Tenant;
            """,connection);
        seed.Parameters.AddWithValue("@Business",business);seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        seed.Parameters.AddWithValue("@Actor",fixture.UserId);seed.Parameters.AddWithValue("@Role",fixture.RoleId);
        seed.Parameters.AddWithValue("@Supplier",fixture.SupplierId);seed.Parameters.AddWithValue("@Zone",zoneId);
        seed.Parameters.AddWithValue("@Rows",JsonSerializer.Serialize(rows));
        try
        {
            var party=(Guid)(await seed.ExecuteScalarAsync())!;
            using var scope=fixture.CreateScope();var service=scope.ServiceProvider.GetRequiredService<AccountingService>();
            var references=scope.ServiceProvider.GetRequiredService<ReferenceOptionService>();
            var user=new AccountingUserIdentity(fixture.UserId,fixture.TenantId,business,new HashSet<string>{AccountingPermissionCodes.Read});
            foreach(var version in new[]{1,2})
            {
                var expected=rows.Where(row=>version==1?DateOnly.FromDateTime(row.at.DateTime)==day:row.at>=start&&row.at<end).ToArray();
                using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
                {
                    var page=await service.ListDocumentsAsync(user,day,day,null,null,null,1,100,dateRangeVersion:version);
                    Assert.Equal(expected.Length,page.TotalCount);
                    Assert.Equal(expected.Select(row=>row.id).Order(),page.Items.Select(row=>row.SourceDocumentId).Order());
                    Assert.Equal(version==1?1:2,counter.Count);
                }
                var lines=await service.ListFinancialTraceabilityLinesAsync(user,day,day,null,null,null,1,100,dateRangeVersion:version);
                Assert.Equal(expected.Length,lines.TotalCount);
                Assert.Equal(expected.Select(row=>row.id).Order(),lines.Items.Select(row=>row.SourceDocumentId).Order());
                var filtered=await service.ListDocumentsAsync(user,day,day,null,null,null,1,100,partyId:party,dateRangeVersion:version);
                Assert.Equal(expected.Where(row=>row.type=="ManualAccountingVoucher").Select(row=>row.id).Order(),filtered.Items.Select(row=>row.SourceDocumentId).Order());
                var searched=await service.ListDocumentsAsync(user,day,day,null,null,rows[1].id.ToString("D"),1,100,dateRangeVersion:version);
                Assert.Equal(expected.Count(row=>row.id==rows[1].id),searched.TotalCount);
                var options=await references.ListContextualAsync(new CatalogUserIdentity(user.UserId,user.TenantId,business,user.Permissions),
                    "accounting-document-type",day,day,dateRangeVersion:version);
                Assert.Equal(expected.Any(row=>row.type=="RestaurantTip"),options.Any(option=>option.Code=="RestaurantTip"));
                using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Read);
                client.DefaultRequestHeaders.Remove("X-Business-Id");client.DefaultRequestHeaders.Add("X-Business-Id",business.ToString("D"));
                foreach(var resource in new[]{"documents","reports/financial-traceability-lines"})
                {
                    using var response=await client.GetAsync($"/api/commerce/v1/accounting/{resource}?from={text}&to={text}&pageSize=100&dateRangeVersion={version}");
                    Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                    var body=await response.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal(expected.Length,body.GetProperty("totalCount").GetInt32());
                }
                using var invalid=await client.GetAsync($"/api/commerce/v1/accounting/documents?from={text}&to={text}&dateRangeVersion=3");
                Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
                using var invalidCatalog=await client.GetAsync($"/api/commerce/v1/reference-options/accounting-document-type?contextual=true&from={text}&to={text}&dateRangeVersion=3");
                Assert.Equal(HttpStatusCode.BadRequest,invalidCatalog.StatusCode);
            }
            var outside=await service.ListDocumentsAsync(user with {BusinessId=fixture.BusinessId},day,day,null,null,rows[1].id.ToString("D"),1,100,dateRangeVersion:2);
            Assert.Empty(outside.Items);
            await using var verify=new SqlCommand("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE TenantId=@Tenant AND BusinessId=@Business; SELECT COUNT(*) FROM dbo.AccountingEntries WHERE TenantId=@Tenant AND BusinessId=@Business;",connection);
            verify.Parameters.AddWithValue("@Tenant",fixture.TenantId);verify.Parameters.AddWithValue("@Business",business);
            await using var reader=await verify.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal(rows.Length,reader.GetInt32(0));
            Assert.True(await reader.NextResultAsync());Assert.True(await reader.ReadAsync());Assert.Equal(0,reader.GetInt32(0));
        }
        finally
        {
            await fixture.DeactivateReadFixtureBusinessesAsync([business]);
        }
    }
}
