using Auraly.Application.Sales;

namespace Auraly.Foundation.Tests;

public sealed class CommercialReportingDesignTests
{
    [Theory]
    [InlineData("SalesInvoice")]
    [InlineData("SalesReceipt")]
    [InlineData("SalesReturn")]
    [InlineData("GoodsReceipt")]
    [InlineData("PurchaseReturn")]
    public void Reporting_policy_accepts_only_operationally_projected_sources(string sourceType)
    {
        Assert.True(SalesReportingProcessingPolicy.Supports(sourceType));
    }

    [Theory]
    [InlineData("ServiceInvoice")]
    [InlineData("RouteVisit")]
    [InlineData("SellerOrder")]
    [InlineData("CommercialCoveragePlan")]
    public void Reporting_policy_rejects_sources_outside_the_operations_engine(string sourceType)
    {
        Assert.False(SalesReportingProcessingPolicy.Supports(sourceType));
    }

    [Fact]
    public void Only_operational_document_handlers_create_reporting_jobs()
    {
        var persistence = Path.Combine(FindRepositoryRoot(), "src", "Infrastructure",
            "Auraly.Infrastructure.Persistence");
        var owners = Directory.EnumerateFiles(persistence, "*.cs")
            .Where(path => File.ReadAllText(path).Contains(
                "SqlSalesReportingJobWriter.InsertAsync", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "SqlGoodsReceiptDocumentHandler.cs",
            "SqlPosSaleDocumentHandler.cs",
            "SqlPurchaseReturnDocumentHandler.cs",
            "SqlSalesReturnDocumentHandler.cs"
        }, owners);
    }

    [Fact]
    public void Service_invoice_reuses_the_sales_header_without_weakening_product_lines()
    {
        var root = FindRepositoryRoot();
        var header = File.ReadAllText(Path.Combine(root, "database", "Auraly.Database",
            "Tables", "SalesDocuments.sql"));
        var serviceLines = File.ReadAllText(Path.Combine(root, "database", "Auraly.Database",
            "Tables", "SalesDocumentServiceLines.sql"));
        var productLines = File.ReadAllText(Path.Combine(root, "database", "Auraly.Database",
            "Tables", "SalesDocumentLines.sql"));

        Assert.Contains("N'ServiceInvoice'", header, StringComparison.Ordinal);
        Assert.Contains("SalesDocumentServiceLines", serviceLines, StringComparison.Ordinal);
        Assert.Contains("BillableServiceId", serviceLines, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductId", serviceLines, StringComparison.Ordinal);
        Assert.Contains("ProductId", productLines, StringComparison.Ordinal);
        Assert.DoesNotContain("BillableServiceId", productLines, StringComparison.Ordinal);
    }

    [Fact]
    public void Seller_and_supplier_scope_is_resolved_in_the_reporting_store()
    {
        var source=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"src","Infrastructure",
            "Auraly.Infrastructure.Persistence","SqlSalesReportingStore.cs"));

        Assert.Contains("dbo.AppUsers",source,StringComparison.Ordinal);
        Assert.Contains("dbo.CommerceSellers",source,StringComparison.Ordinal);
        Assert.Contains("dbo.Suppliers",source,StringComparison.Ordinal);
        Assert.Contains("cannot widen the report scope",source,StringComparison.Ordinal);
        Assert.Contains("maps ambiguously to both seller and supplier",source,StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current=new DirectoryInfo(AppContext.BaseDirectory);
        while(current is not null)
        {
            if(File.Exists(Path.Combine(current.FullName,"Auraly.Commerce.sln")))return current.FullName;
            current=current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
