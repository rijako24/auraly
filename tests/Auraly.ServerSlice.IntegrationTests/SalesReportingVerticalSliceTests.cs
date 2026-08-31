using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Auraly.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SalesReportingSliceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly sales reporting slice";
}

[Collection(SalesReportingSliceCollection.Name)]
[Trait("EngineCertification", "Reporting")]
public sealed class SalesReportingVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Read_all_permission_keeps_an_administrator_unscoped_when_their_party_is_also_a_seller()
    {
        var partyId=Guid.NewGuid();var sellerId=Guid.NewGuid();var userId=Guid.NewGuid();var suffix=Guid.NewGuid().ToString("N");
        await using(var connection=new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="""
                INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Administrador vendedor',N'Administrador',N'Vendedor',N'Complete',1,@ActorId,SYSDATETIMEOFFSET());
                INSERT dbo.AppUsers(UserId,TenantId,PartyId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
                VALUES(@UserId,@TenantId,@PartyId,@Username,UPPER(@Username),@Email,UPPER(@Email),N'Administrador',N'Vendedor',0,1,1,SYSUTCDATETIME());
                INSERT dbo.CommerceSellers(SellerId,BusinessId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
                VALUES(@SellerId,@BusinessId,@PartyId,@Code,N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());
                """;
            command.Parameters.AddWithValue("@PartyId",partyId);command.Parameters.AddWithValue("@SellerId",sellerId);
            command.Parameters.AddWithValue("@UserId",userId);command.Parameters.AddWithValue("@TenantId",fixture.TenantId);
            command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);command.Parameters.AddWithValue("@ActorId",fixture.UserId);
            command.Parameters.AddWithValue("@Username",$"admin-seller-{suffix}");command.Parameters.AddWithValue("@Email",$"admin-seller-{suffix}@auraly.test");
            command.Parameters.AddWithValue("@Code",$"AS-{suffix[..10]}");await command.ExecuteNonQueryAsync();
        }
        using var scoped=fixture.CreateUserClient(userId,SalesReportingPermissionCodes.Read);
        using var scopedResponse=await scoped.GetAsync("/api/commerce/v1/sales-reports/supplier-impact?from=2026-07-27&to=2026-07-27");
        Assert.Equal(HttpStatusCode.Forbidden,scopedResponse.StatusCode);
        using var administrator=fixture.CreateUserClient(userId,SalesReportingPermissionCodes.Read,SalesReportingPermissionCodes.ReadAll);
        using var response=await administrator.GetAsync("/api/commerce/v1/sales-reports/supplier-impact?from=2026-07-27&to=2026-07-27");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<SupplierImpactOverview>());
    }

    [Fact]
    public async Task Seller_order_is_projected_and_aggregated_by_seller()
    {
        var orderId=Guid.NewGuid();var sellerId=Guid.NewGuid();var customerId=Guid.NewGuid();
        var source=new CommercialOrderProjectionSource(fixture.TenantId,fixture.BusinessId,orderId,
            new DateOnly(2026,7,27),new DateTimeOffset(2026,7,27,14,0,0,TimeSpan.Zero),"PED-REPORT-1",
            sellerId,"Vendedor proyectado",customerId,"Cliente proyectado",null,125000m,2,false);
        var payload=System.Text.Json.JsonSerializer.Serialize(source,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        await using(var connection=new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString))
        {await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="""
          INSERT reporting.SalesReportingJobs(SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,AttemptCount,CreatedAt)
          VALUES(NEWID(),@BusinessId,@OrderId,N'SellerOrder',1,@Hash,@Payload,N'Pending',0,SYSDATETIMEOFFSET());
          """;command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);command.Parameters.AddWithValue("@OrderId",orderId);
          command.Parameters.Add("@Hash",System.Data.SqlDbType.Binary,32).Value=hash;command.Parameters.AddWithValue("@Payload",payload);await command.ExecuteNonQueryAsync();}
        await fixture.Services.GetRequiredService<SqlSalesReportingProcessor>().ProcessAsync(orderId,"SellerOrder",fixture.BusinessId,1,CancellationToken.None);
        using var client=fixture.CreateAdminClient(SalesReportingPermissionCodes.Read);
        var rows=await client.GetFromJsonAsync<SellerOrderReportRow[]>("/api/commerce/v1/sales-reports/seller-orders?from=2026-07-27&to=2026-07-27");
        var row=Assert.Single(rows??[],x=>x.SellerId==sellerId);Assert.Equal(1,row.OrderCount);Assert.Equal(125000m,row.OrderAmount);Assert.Equal(1,row.ConfirmedCount);Assert.Equal(0,row.InvoicedCount);
    }

    [Fact]
    public async Task Confirmed_sale_is_projected_and_reported_without_operational_joins()
    {
        var sale = fixture.CreateValidRequest(9_901);
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(
                         fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT_BIG(*)
                FROM reporting.SalesReportingJobs
                WHERE SourceDocumentId=@DocumentId
                  AND SourceDocumentType IN (N'SalesInvoice',N'SalesReceipt')
                  AND Status=N'Projected'
                  AND AttemptCount=1;
                """;
            command.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));

            command.CommandText = """
                SELECT WindowEndDate,NetUnitsSold30Days,NetUnitsSold90Days,DailyDemand90Days
                FROM reporting.ProductRotationSnapshots
                WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
                """;
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            await using (var rotation = await command.ExecuteReaderAsync())
            {
                Assert.True(await rotation.ReadAsync());
                var soldQuantity = sale.Lines.Sum(line => line.Quantity);
                Assert.Equal(new DateTime(2026, 7, 27), rotation.GetDateTime(0));
                Assert.Equal(soldQuantity, rotation.GetDecimal(1));
                Assert.Equal(soldQuantity, rotation.GetDecimal(2));
                Assert.Equal(decimal.Round(soldQuantity / 90m, 6), rotation.GetDecimal(3));
            }

            command.CommandText = """
                SELECT AttributionSnapshotVersion,SupplierIdSnapshot,UnitCostSnapshot
                FROM dbo.SalesDocumentLines
                WHERE DocumentId=@DocumentId AND LineNumber=1;
                """;
            await using var attribution = await command.ExecuteReaderAsync();
            Assert.True(await attribution.ReadAsync());
            Assert.Equal((short)1, attribution.GetInt16(0));
            Assert.Equal(fixture.SupplierId, attribution.GetGuid(1));
            Assert.True(attribution.GetDecimal(2) >= 0);
        }

        using var reporting = fixture.CreateAdminClient(
            SalesReportingPermissionCodes.Read);
        using var summaryResponse = await reporting.GetAsync(
            "/api/commerce/v1/sales-reports/summary?from=2026-07-27&to=2026-07-27");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var summary = await summaryResponse.Content
            .ReadFromJsonAsync<SalesReportSummary>();
        Assert.NotNull(summary);
        Assert.Equal(1, summary.Current.DocumentCount);
        Assert.Equal(11_900m, summary.Current.NetTotalSales);
        Assert.Single(summary.Trend);
        Assert.NotNull(summary.ProjectedThrough);

        using var productResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/breakdown?from=2026-07-27&to=2026-07-27&dimension=product&productId={fixture.ProductId:D}");
        Assert.Equal(HttpStatusCode.OK, productResponse.StatusCode);
        var products = await productResponse.Content
            .ReadFromJsonAsync<SalesReportBreakdownRow[]>();
        var product = Assert.Single(products ?? []);
        Assert.Equal(fixture.ProductId, Guid.Parse(product.Key));
        Assert.Equal(11_900m, product.NetSales);

        using (var scope = fixture.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISalesReportingStore>();
            var directPayments = await store.GetBreakdownAsync(
                new SalesReportingUserIdentity(
                    fixture.UserId, fixture.TenantId, fixture.BusinessId,
                    new HashSet<string> { SalesReportingPermissionCodes.Read }),
                new SalesReportFilter(
                    new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 27),
                    ProductId: fixture.ProductId),
                SalesReportingDimensions.PaymentMethod,
                100,
                CancellationToken.None);
            Assert.Single(directPayments);
        }

        using var paymentsResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/breakdown?from=2026-07-27&to=2026-07-27&dimension=payment-method&productId={fixture.ProductId:D}");
        Assert.True(paymentsResponse.StatusCode == HttpStatusCode.OK,
            await paymentsResponse.Content.ReadAsStringAsync());
        var payments = await paymentsResponse.Content.ReadFromJsonAsync<SalesReportBreakdownRow[]>();
        var cash = Assert.Single(payments ?? []);
        Assert.Equal("Cash", cash.Key);
        Assert.Equal(11_900m, cash.NetSales);

        using var taxesResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/breakdown?from=2026-07-27&to=2026-07-27&dimension=tax&supplierId={fixture.SupplierId:D}");
        Assert.Equal(HttpStatusCode.OK, taxesResponse.StatusCode);
        var taxes = await taxesResponse.Content.ReadFromJsonAsync<SalesReportBreakdownRow[]>();
        var tax = Assert.Single(taxes ?? []);
        Assert.Equal(10_000m, tax.NetUntaxedSales);
        Assert.Equal(1_900m, tax.Tax);
        Assert.Equal(11_900m, tax.NetSales);

        using var excludedResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/breakdown?from=2026-07-27&to=2026-07-27&dimension=payment-method&productId={Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.OK, excludedResponse.StatusCode);
        Assert.Empty(await excludedResponse.Content.ReadFromJsonAsync<SalesReportBreakdownRow[]>() ?? []);

        using var documentsResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/documents?from=2026-07-27&to=2026-07-27&page=1&pageSize=25&supplierId={fixture.SupplierId:D}");
        Assert.Equal(HttpStatusCode.OK, documentsResponse.StatusCode);
        var documents = await documentsResponse.Content
            .ReadFromJsonAsync<SalesReportDocumentPage>();
        Assert.NotNull(documents);
        Assert.Equal(1, documents.TotalCount);
        Assert.Equal(sale.DocumentId, Assert.Single(documents.Items).DocumentId);

        using var detailResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/documents/{sale.DocumentId:D}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content
            .ReadFromJsonAsync<SalesReportDocumentDetail>();
        Assert.NotNull(detail);
        Assert.Equal(sale.DocumentId, detail.Document.DocumentId);
        Assert.Single(detail.Lines);

        using var hourlyResponse = await reporting.GetAsync(
            "/api/commerce/v1/sales-reports/breakdown?from=2026-07-27&to=2026-07-27&dimension=hour");
        Assert.Equal(HttpStatusCode.OK, hourlyResponse.StatusCode);
        var hours = await hourlyResponse.Content
            .ReadFromJsonAsync<SalesReportBreakdownRow[]>();
        Assert.Equal(11_900m, Assert.Single(hours ?? []).NetSales);

        using var todayResponse = await reporting.GetAsync(
            "/api/commerce/v1/sales-reports/today");
        Assert.Equal(HttpStatusCode.OK, todayResponse.StatusCode);
        Assert.NotNull(await todayResponse.Content
            .ReadFromJsonAsync<SalesTodayOverview>());
    }
}
