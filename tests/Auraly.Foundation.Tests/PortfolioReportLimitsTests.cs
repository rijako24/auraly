using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Foundation.Tests;

public sealed class PortfolioReportLimitsTests
{
    [Fact]
    public void Export_limit_includes_optional_application_rows_without_truncating_them()
    {
        PortfolioReportLimits.ValidateExport(5_000, 0);
        PortfolioReportLimits.ValidateExport(1, 4_999);
        Assert.Throws<PortfolioReportLimitException>(() => PortfolioReportLimits.ValidateExport(1, 5_000));
        Assert.Throws<PortfolioReportLimitException>(() => PortfolioReportLimits.ValidateExport(5_001, 0));
    }
}
