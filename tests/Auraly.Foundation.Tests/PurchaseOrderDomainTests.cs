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
}
