using Auraly.Domain.Purchasing;

namespace Auraly.Foundation.Tests;

public sealed class PurchaseOrderDomainTests
{
    [Fact]
    public void Calculates_multiple_lines_and_tax_from_the_frozen_order_values()
    {
        var result = PurchaseOrderCalculator.Calculate([
            (Guid.NewGuid(), 1, Guid.NewGuid(), "Caja A", 10m, 5_000m, 5_000m, "01", 19m, "DeductibleInputVat"),
            (Guid.NewGuid(), 2, Guid.NewGuid(), "Unidad B", 2m, 2_500m, 0m, "00", 0m, "NotApplicable")
        ]);

        Assert.Equal(50_000m, result.NetAmount);
        Assert.Equal(8_550m, result.TaxAmount);
        Assert.Equal(58_550m, result.GrandTotal);
    }

    [Theory]
    [InlineData(10, 0, 0, 10)]
    [InlineData(10, 8, 0, 2)]
    [InlineData(10, 12, 0, 0)]
    [InlineData(10, 8, 2, 0)]
    public void Remaining_never_becomes_negative(decimal ordered, decimal received,
        decimal cancelled, decimal expected)
    {
        Assert.Equal(expected, PurchaseOrderCalculator.Remaining(ordered, received, cancelled));
    }

    [Fact]
    public void Derives_open_partial_and_received_statuses()
    {
        Assert.Equal("Open", PurchaseOrderCalculator.Status([(10m, 0m, 0m), (5m, 0m, 0m)]));
        Assert.Equal("PartiallyReceived", PurchaseOrderCalculator.Status([(10m, 8m, 0m), (5m, 0m, 0m)]));
        Assert.Equal("Received", PurchaseOrderCalculator.Status([(10m, 10m, 0m), (5m, 5m, 0m)]));
        Assert.Equal("Received", PurchaseOrderCalculator.Status([(10m, 12m, 0m)]));
    }

    [Theory]
    [InlineData(2, 3, 0, 1, 7, 11, 11)]
    [InlineData(2, 3, 4, 6, 7, 12, 2)]
    [InlineData(2, 20, 0, 1, 7, 0, 0)]
    [InlineData(0, 0, 0, 12, 7, 0, 0)]
    public void Suggestion_uses_rotation_stock_incoming_and_supplier_presentation(
        decimal dailyDemand, decimal stock, decimal incoming, decimal presentation,
        int days, decimal expectedQuantity, decimal expectedPresentations)
    {
        var result=PurchaseOrderCalculator.Suggest(
            dailyDemand,stock,incoming,presentation,days);
        Assert.Equal(expectedQuantity,result.Quantity);
        Assert.Equal(expectedPresentations,result.PresentationQuantity);
    }

    [Fact]
    public void Forecast_prioritizes_recent_rotation_without_discarding_the_stable_window()
    {
        Assert.Equal(3.4m,PurchaseOrderCalculator.ForecastDailyDemand(120m,180m));
        Assert.Equal(0m,PurchaseOrderCalculator.ForecastDailyDemand(-10m,-20m));
    }
}
