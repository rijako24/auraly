using Auraly.Application.Catalog;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingTraceabilityFilterTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Voucher_filter_precedes_pagination_and_restaurant_options_preserve_only_actual_history()
    {
        var businessId=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var seed=new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
            VALUES(@Business,@Tenant,N'Filtros financieros',N'',N'',N'',N'',N'');
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
            SELECT NEWID(),SourceType,@Tenant,@Business,Payload,HASHBYTES('SHA2_256',CONVERT(varbinary(max),Payload)),OccurredAt,SYSDATETIMEOFFSET()
            FROM (VALUES
              (N'ManualAccountingVoucher',N'{"PayloadVersion":4,"VoucherTypeCode":"Accounting"}',CONVERT(datetimeoffset,'2026-11-02T10:00:00+00:00')),
              (N'ManualAccountingVoucher',N'{"PayloadVersion":4,"VoucherTypeCode":"Disbursement"}',CONVERT(datetimeoffset,'2026-11-03T10:00:00+00:00')),
              (N'ManualAccountingVoucher',N'{"PayloadVersion":2,"VoucherTypeCode":"Accounting"}',CONVERT(datetimeoffset,'2026-11-04T10:00:00+00:00')),
              (N'RestaurantTip',N'{}',CONVERT(datetimeoffset,'2026-11-05T10:00:00+00:00'))
            ) source(SourceType,Payload,OccurredAt);
            INSERT accounting.VoucherDrafts(DocumentId,TenantId,BusinessId,DocumentType,OccurredAt,ConceptCode,Description,
              CurrencyCode,CreatedBy,CreatedAt,UpdatedBy,UpdatedAt,ContentHash,PayloadVersion,VoucherTypeCode)
            VALUES(NEWID(),@Tenant,@Business,N'ManualAccountingVoucher','2026-11-01T10:00:00+00:00',N'General',N'Borrador',
              N'COP',@Actor,SYSDATETIMEOFFSET(),@Actor,SYSDATETIMEOFFSET(),HASHBYTES('SHA2_256',N'draft'),4,N'Accounting');
            """,connection);
        seed.Parameters.AddWithValue("@Business",businessId);seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@Actor",fixture.UserId);
        await seed.ExecuteNonQueryAsync();
        using var scope=fixture.CreateScope();var service=scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user=new AccountingUserIdentity(fixture.UserId,fixture.TenantId,businessId,new HashSet<string>{AccountingPermissionCodes.Read});
        var from=new DateOnly(2026,11,1);var to=new DateOnly(2026,11,30);
        AccountingDocumentPage first;
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            first=await service.ListDocumentsAsync(user,from,to,"ManualAccountingVoucher",null,null,1,1,voucherTypeCode:"Accounting");
            Assert.Equal(1,counter.Count);
        }
        var second=await service.ListDocumentsAsync(user,from,to,"ManualAccountingVoucher",null,null,2,1,voucherTypeCode:"Accounting");
        Assert.Equal(2,first.TotalCount);Assert.Equal(2,second.TotalCount);
        Assert.Equal("Accounting",Assert.Single(first.Items).VoucherTypeCode);
        Assert.Equal("Accounting",Assert.Single(second.Items).VoucherTypeCode);
        Assert.NotEqual(first.Items[0].SourceDocumentId,second.Items[0].SourceDocumentId);
        var unrelated=await service.ListDocumentsAsync(user,from,to,"RestaurantTip",null,null,1,25,voucherTypeCode:"Accounting");
        Assert.Empty(unrelated.Items);Assert.Equal(0,unrelated.TotalCount);
        await Assert.ThrowsAsync<AccountingValidationException>(()=>service.ListDocumentsAsync(user,from,to,null,null,null,1,25,voucherTypeCode:"not-in-catalog"));
        var references=scope.ServiceProvider.GetRequiredService<ReferenceOptionService>();
        var identity=new CatalogUserIdentity(user.UserId,user.TenantId,user.BusinessId,user.Permissions);
        var current=await references.ListContextualAsync(identity,"accounting-document-type",null,null);
        Assert.DoesNotContain(current,option=>option.Code=="RestaurantTip");
        var history=await references.ListContextualAsync(identity,"accounting-document-type",from,to);
        Assert.EndsWith("(histórico)",Assert.Single(history.Where(option=>option.Code=="RestaurantTip")).Label);
        Assert.DoesNotContain(history,option=>option.Code is "RestaurantTipReversal" or "RestaurantTipSettlement");
    }
}
