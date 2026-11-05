using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SalesReportHistorySliceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Sales reporting immutable history";
}

[Collection(SalesReportHistorySliceCollection.Name)]
public sealed class SalesReportCategorySnapshotTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Category_hierarchy_is_frozen_at_sale_and_rebuild_does_not_reclassify_history_or_repeat_stock()
    {
        var area=Guid.NewGuid();var line=Guid.NewGuid();var group=Guid.NewGuid();var subgroup=Guid.NewGuid();var changedArea=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=new SqlCommand("SELECT ProductCategoryId FROM dbo.Products WHERE ProductId=@Product;",connection);
        command.Parameters.AddWithValue("@Product",fixture.ProductId);var original=await command.ExecuteScalarAsync();
        command.CommandText="""
            INSERT dbo.ProductCategories(ProductCategoryId,TenantId,ParentProductCategoryId,Name)
            VALUES(@Area,@Tenant,NULL,CONCAT(N'Área ',@Area)),(@Line,@Tenant,@Area,CONCAT(N'Línea ',@Line)),
              (@Group,@Tenant,@Line,CONCAT(N'Grupo ',@Group)),(@Subgroup,@Tenant,@Group,CONCAT(N'Subgrupo ',@Subgroup)),
              (@Changed,@Tenant,NULL,CONCAT(N'Otra área ',@Changed));
            UPDATE dbo.Products SET ProductCategoryId=@Subgroup WHERE ProductId=@Product;
            """;
        command.Parameters.AddWithValue("@Tenant",fixture.TenantId);command.Parameters.AddWithValue("@Area",area);
        command.Parameters.AddWithValue("@Line",line);command.Parameters.AddWithValue("@Group",group);
        command.Parameters.AddWithValue("@Subgroup",subgroup);command.Parameters.AddWithValue("@Changed",changedArea);
        await command.ExecuteNonQueryAsync();
        try
        {
            var sale=fixture.CreateValidRequest(9_632);
            using(var upload=fixture.CreateUploadMessage(sale))using(var response=await fixture.CreateClient().SendAsync(upload))response.EnsureSuccessStatusCode();
            command.Parameters.AddWithValue("@Document",sale.DocumentId);
            command.CommandText="SELECT CategoryHierarchySnapshotJson FROM dbo.SalesDocumentLines WHERE DocumentId=@Document AND LineNumber=1;";
            var snapshot=(string)(await command.ExecuteScalarAsync())!;
            using(var json=JsonDocument.Parse(snapshot))
            {Assert.Equal(area,json.RootElement.GetProperty("AreaId").GetGuid());Assert.Equal(subgroup,json.RootElement.GetProperty("SubgroupId").GetGuid());}
            command.CommandText="UPDATE dbo.Products SET ProductCategoryId=@Changed WHERE ProductId=@Product;";await command.ExecuteNonQueryAsync();
            using var scope=fixture.CreateScope();await scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>()
                .RebuildAsync(fixture.TenantId,fixture.BusinessId,CancellationToken.None);
            command.CommandText="SELECT CategoryHierarchyJson FROM reporting.SalesReportLineFacts WHERE SourceDocumentId=@Document AND SourceLineNumber=1;";
            Assert.Equal(snapshot,await command.ExecuteScalarAsync());
            command.CommandText="SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Document;";
            Assert.Equal(1,Convert.ToInt32(await command.ExecuteScalarAsync()));
            var user=new SalesReportingUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
                new HashSet<string>{SalesReportingPermissionCodes.Read,SalesReportingPermissionCodes.ReadAll});
            var reports=scope.ServiceProvider.GetRequiredService<SalesReportingService>();
            var filter=new SalesReportFilter(new(2026,7,26),new(2026,7,29),AreaIds:[area],SubgroupIds:[subgroup]);
            Assert.Single((await reports.ExploreAsync(user,new(filter))).Items);
            Assert.Empty((await reports.ExploreAsync(user,new(filter with {AreaIds=[changedArea]}))).Items);
        }
        finally
        {
            command.CommandText="UPDATE dbo.Products SET ProductCategoryId=@Original WHERE ProductId=@Product;";
            command.Parameters.AddWithValue("@Original",original??DBNull.Value);await command.ExecuteNonQueryAsync();
        }
    }
}
