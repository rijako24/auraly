using Auraly.Application.Sales;

namespace Auraly.Foundation.Tests;

public sealed class CommercialReportingDesignTests
{
    [Theory]
    [InlineData("SalesInvoice")]
    [InlineData("SalesReceipt")]
    [InlineData("SalesReturn")]
    [InlineData("RouteVisit")]
    [InlineData("SellerOrder")]
    [InlineData("CommercialCoveragePlan")]
    [InlineData("GoodsReceipt")]
    [InlineData("PurchaseReturn")]
    public void Reporting_policy_accepts_every_canonical_commercial_source(string sourceType)
    {
        Assert.True(SalesReportingProcessingPolicy.Supports(sourceType));
    }

    [Fact]
    public void Semantic_reports_are_not_aliases_of_the_sales_analytics_page()
    {
        var root=FindRepositoryRoot();
        foreach(var report in new[]{"sellers","customers","supplier-impact"})
        {
            var page=File.ReadAllText(Path.Combine(root,"admin","src","app","(dashboard)",
                "dashboard","reports",report,"page.tsx"));
            Assert.DoesNotContain("../../analytics/page",page,StringComparison.Ordinal);
            Assert.Contains("commercial-semantic-reports",page,StringComparison.Ordinal);
        }
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
