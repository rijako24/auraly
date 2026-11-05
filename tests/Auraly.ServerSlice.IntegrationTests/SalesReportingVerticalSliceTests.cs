using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_null_uploaded_cost_preserves_ledger_cost_or_explicitly_unknown_generic_cost(bool generic)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var sale = fixture.CreateValidRequest(generic ? 9_599 : 9_598);
        bool wasGeneric = false, managedStock = true;
        if (generic)
        {
            await using var configure = connection.CreateCommand();
            configure.CommandText = """
                UPDATE dbo.Products SET IsGenericProduct=1,ManageStock=0
                  OUTPUT deleted.IsGenericProduct,deleted.ManageStock WHERE ProductId=@Product;
                """;
            configure.Parameters.AddWithValue("@Product", fixture.ProductId);
            await using var state = await configure.ExecuteReaderAsync();
            Assert.True(await state.ReadAsync());
            wasGeneric = state.GetBoolean(0); managedStock = state.GetBoolean(1);
            sale = sale with { Lines = sale.Lines.Select(line => line with {
                IsGenericProductSnapshot = true, DocumentUnitCost = 6_000m }).ToArray() };
        }
        try
        {
            using var upload = fixture.CreateUploadMessage(sale);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            if (generic)
            {
                await using var restore = connection.CreateCommand();
                restore.CommandText = "UPDATE dbo.Products SET IsGenericProduct=@Generic,ManageStock=@Stock WHERE ProductId=@Product;";
                restore.Parameters.AddWithValue("@Generic", wasGeneric); restore.Parameters.AddWithValue("@Stock", managedStock);
                restore.Parameters.AddWithValue("@Product", fixture.ProductId);
                await restore.ExecuteNonQueryAsync();
            }
        }
        await using var source = connection.CreateCommand();
        source.CommandText = """
            SELECT payload.PayloadJson FROM reporting.SalesReportingJobs report
            JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=report.SourceDocumentId
              AND payload.DocumentType=report.SourceDocumentType AND payload.BusinessId=report.BusinessId
            WHERE report.SourceDocumentId=@Document;
            """;
        source.Parameters.AddWithValue("@Document", sale.DocumentId);
        var legacy = JsonNode.Parse(Assert.IsType<string>(await source.ExecuteScalarAsync()))!;
        foreach (var line in legacy["lines"]!.AsArray()) line!["documentUnitCost"] = null;
        var payload = legacy.ToJsonString();
        Assert.Throws<JsonException>(() => PosSaleContractSerializer.Deserialize(payload));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        // Simulate an accepted historical source only in this disposable fixture.
        // Do not alter inventory: the generic sale naturally has no ledger entry.
        source.CommandText = """
            UPDATE payload SET PayloadJson=@Payload,PayloadHash=@Hash FROM dbo.DocumentProcessingPayloads payload
            JOIN reporting.SalesReportingJobs report ON report.SourceDocumentId=payload.DocumentId
              AND report.SourceDocumentType=payload.DocumentType AND report.BusinessId=payload.BusinessId
            WHERE report.SourceDocumentId=@Document;
            UPDATE reporting.SalesReportingJobs SET SourcePayloadHash=@Hash WHERE SourceDocumentId=@Document;
            UPDATE dbo.SalesDocuments SET PayloadHash=@Hash WHERE DocumentId=@Document;
            IF @Generic=1 UPDATE dbo.SalesDocumentLines SET UnitCostSnapshot=NULL WHERE DocumentId=@Document;
            """;
        source.Parameters.AddWithValue("@Payload", payload); source.Parameters.AddWithValue("@Hash", hash);
        source.Parameters.AddWithValue("@Generic", generic);
        await source.ExecuteNonQueryAsync();
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
        for (var replay = 0; replay < 2; replay++)
        {
            await processor.RebuildAsync(fixture.TenantId, fixture.BusinessId, default);
            await using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT fact.CostAvailable,report.RecognizedCostAmount,report.MissingCostLineCount,
                  (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Document),
                  (SELECT -SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Document AND MovementType=N'Sale'),
                  payload.PayloadJson,payload.PayloadHash,
                  CONVERT(bit,IIF(report.TotalAmount=sale.PayableAmount AND report.UntaxedAmount=sale.UntaxedAmount
                    AND report.TaxAmount=sale.TaxAmount,1,0))
                FROM reporting.SalesReportDocuments report
                JOIN reporting.SalesReportLineFacts fact ON fact.SourceDocumentId=report.DocumentId
                JOIN dbo.SalesDocuments sale ON sale.DocumentId=report.DocumentId
                JOIN reporting.SalesReportingJobs source ON source.SourceDocumentId=report.DocumentId
                JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=source.SourceDocumentId
                  AND payload.DocumentType=source.SourceDocumentType AND payload.BusinessId=source.BusinessId
                WHERE report.DocumentId=@Document;
                """;
            verify.Parameters.AddWithValue("@Document", sale.DocumentId);
            await using var result = await verify.ExecuteReaderAsync();
            Assert.True(await result.ReadAsync()); Assert.Equal(!generic, result.GetBoolean(0));
            if (generic) Assert.True(result.IsDBNull(1));
            else Assert.Equal(result.GetDecimal(4), result.GetDecimal(1));
            Assert.Equal(generic ? 1L : 0L, result.GetInt64(2));
            Assert.Equal(generic ? 0 : 1, result.GetInt32(3));
            Assert.Equal(payload, result.GetString(5)); Assert.Equal(hash, (byte[])result.GetValue(6));
            Assert.True(result.GetBoolean(7)); Assert.False(await result.ReadAsync());
        }
    }

    [Fact]
    public async Task Recognized_sale_cost_comes_from_the_canonical_ledger_instead_of_a_stale_uploaded_estimate()
    {
        var original=fixture.CreateValidRequest(9_597);
        var sale=original with {Lines=original.Lines.Select(line=>line with {DocumentUnitCost=1m}).ToArray()};
        using(var upload=fixture.CreateUploadMessage(sale))
        using(var response=await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await using var connection=new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var query=connection.CreateCommand();query.CommandText="""
            SELECT movement.ValueChange,line.UnitCostSnapshot,fact.RecognizedCostAmount,document.RecognizedCostAmount,
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Document)
            FROM dbo.SalesDocumentLines line
            JOIN dbo.InventoryMovements movement ON movement.DocumentId=line.DocumentId AND movement.LineNumber=line.LineNumber AND movement.MovementType=N'Sale'
            JOIN reporting.SalesReportLineFacts fact ON fact.SourceDocumentId=line.DocumentId AND fact.SourceLineNumber=line.LineNumber
            JOIN reporting.SalesReportDocuments document ON document.DocumentId=line.DocumentId
            WHERE line.DocumentId=@Document;
            """;
        query.Parameters.AddWithValue("@Document",sale.DocumentId);
        await using var data=await query.ExecuteReaderAsync();Assert.True(await data.ReadAsync());
        var actual=-data.GetDecimal(0);Assert.NotEqual(sale.Lines[0].Quantity,actual);
        Assert.Equal(actual,data.GetDecimal(1)*sale.Lines[0].Quantity);
        Assert.Equal(actual,data.GetDecimal(2));Assert.Equal(actual,data.GetDecimal(3));Assert.Equal(1,data.GetInt32(4));
    }

    [Fact]
    public async Task Read_all_permission_allows_a_platform_actor_without_a_local_tenant_user()
    {
        var externalPlatformUserId=Guid.NewGuid();
        using var scope=fixture.CreateScope();
        var store=scope.ServiceProvider.GetRequiredService<ISalesReportingStore>();
        var scopedIdentity=new SalesReportingUserIdentity(
            externalPlatformUserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{SalesReportingPermissionCodes.Read});
        await Assert.ThrowsAsync<SalesReportingForbiddenException>(()=>
            store.GetSupplierImpactAsync(scopedIdentity,new DateOnly(2026,7,27),
                new DateOnly(2026,7,27),CancellationToken.None));

        var administratorIdentity=scopedIdentity with {Permissions=new HashSet<string>{
            SalesReportingPermissionCodes.Read,SalesReportingPermissionCodes.ReadAll}};
        Assert.NotNull(await store.GetSupplierImpactAsync(administratorIdentity,
            new DateOnly(2026,7,27),new DateOnly(2026,7,27),CancellationToken.None));
    }

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
                INSERT dbo.CommerceSellers(SellerId,TenantId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
                VALUES(@SellerId,@TenantId,@PartyId,@Code,N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());
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
    public async Task Confirmed_sale_is_projected_and_reported_without_operational_joins()
    {
        var sale = fixture.CreateValidRequest(9_582);
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
                SELECT NetQuantity FROM reporting.ProductDailyRotationSnapshots
                WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
                  AND ProductId=@ProductId AND BusinessLocalDate='2026-07-27';
                """;
            Assert.Equal(sale.Lines.Sum(line => line.Quantity),
                (decimal)(await command.ExecuteScalarAsync())!);

            command.CommandText = """
                SELECT line.AttributionSnapshotVersion,line.SupplierIdSnapshot,line.UnitCostSnapshot,
                       fact.RecognizedCostAmount,fact.TotalAmount
                FROM dbo.SalesDocumentLines line
                JOIN reporting.SalesReportLineFacts fact
                  ON fact.SourceDocumentId=line.DocumentId
                 AND fact.SourceLineNumber=line.LineNumber
                 AND fact.MovementType=N'Sale'
                WHERE line.DocumentId=@DocumentId AND line.LineNumber=1;
                """;
            await using var attribution = await command.ExecuteReaderAsync();
            Assert.True(await attribution.ReadAsync());
            Assert.Equal((short)2, attribution.GetInt16(0));
            Assert.Equal(fixture.SupplierId, attribution.GetGuid(1));
            Assert.Equal(sale.Lines[0].DocumentUnitCost, attribution.GetDecimal(2));
            Assert.Equal(
                sale.Lines[0].Quantity * sale.Lines[0].DocumentUnitCost,
                attribution.GetDecimal(3));
            Assert.Equal(
                sale.Lines[0].LineTotal -
                sale.Lines[0].Quantity * sale.Lines[0].DocumentUnitCost,
                attribution.GetDecimal(4) - attribution.GetDecimal(3));
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
        Assert.Equal(4_000m, summary.Current.GrossProfit);
        Assert.Equal(40m, summary.Current.GrossMarginPercent);
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
            await Assert.ThrowsAsync<SalesReportingValidationException>(()=>store.GetBreakdownAsync(
                new SalesReportingUserIdentity(
                    fixture.UserId, fixture.TenantId, fixture.BusinessId,
                    new HashSet<string> { SalesReportingPermissionCodes.Read }),
                new SalesReportFilter(
                    new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 27),
                    ProductId: fixture.ProductId),
                SalesReportingDimensions.PaymentMethod,
                100,
                CancellationToken.None));
        }

        using var paymentsResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/breakdown?from=2026-07-27&to=2026-07-27&dimension=payment-method&productId={fixture.ProductId:D}");
        Assert.Equal(HttpStatusCode.BadRequest,paymentsResponse.StatusCode);
        using var wholePaymentsResponse=await reporting.GetAsync(
            "/api/commerce/v1/sales-reports/breakdown?from=2026-07-27&to=2026-07-27&dimension=payment-method");
        wholePaymentsResponse.EnsureSuccessStatusCode();
        var payments = await wholePaymentsResponse.Content.ReadFromJsonAsync<SalesReportBreakdownRow[]>();
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
        Assert.Equal(HttpStatusCode.BadRequest, excludedResponse.StatusCode);

        using var documentsResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/documents?from=2026-07-27&to=2026-07-27&page=1&pageSize=25&supplierId={fixture.SupplierId:D}");
        Assert.Equal(HttpStatusCode.OK, documentsResponse.StatusCode);
        var documents = await documentsResponse.Content
            .ReadFromJsonAsync<SalesReportDocumentPage>();
        Assert.NotNull(documents);
        Assert.Equal(1, documents.TotalCount);
        var directDocument = Assert.Single(documents.Items);
        Assert.Equal(sale.DocumentId, directDocument.DocumentId);
        Assert.Null(directDocument.RestaurantTipAmount);

        using var detailResponse = await reporting.GetAsync(
            $"/api/commerce/v1/sales-reports/documents/{sale.DocumentId:D}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content
            .ReadFromJsonAsync<SalesReportDocumentDetail>();
        Assert.NotNull(detail);
        Assert.Equal(sale.DocumentId, detail.Document.DocumentId);
        Assert.Equal(0m, detail.Document.RestaurantTipAmount);
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
