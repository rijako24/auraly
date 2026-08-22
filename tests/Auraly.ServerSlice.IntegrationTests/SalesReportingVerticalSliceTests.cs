using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SalesReportingSliceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly sales reporting slice";
}

[Collection(SalesReportingSliceCollection.Name)]
public sealed class SalesReportingVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Confirmed_sale_is_projected_and_reported_without_operational_joins()
    {
        var sale = fixture.CreateValidRequest(9_901);
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

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
    }
}
