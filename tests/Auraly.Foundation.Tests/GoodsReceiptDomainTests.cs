using Auraly.Domain.Inventory;
using Auraly.Domain.Payables;
using Auraly.Domain.Pricing;
using Auraly.Domain.Purchasing;

namespace Auraly.Foundation.Tests;

public sealed class GoodsReceiptDomainTests
{
    [Fact]
    public void Receipt_calculates_discount_tax_and_totals_deterministically()
    {
        var productId = Guid.NewGuid();
        var result = GoodsReceiptCalculator.Calculate([
            (1, productId, " Producto ", 10m, 6_000m, 10_000m, "01", 19m,
                PurchaseTaxTreatment.DeductibleInputVat)
        ]);

        var line = Assert.Single(result.Lines);
        Assert.Equal("Producto", line.Description);
        Assert.Equal(50_000m, line.NetAmount);
        Assert.Equal(9_500m, line.TaxAmount);
        Assert.Equal(59_500m, line.LineTotal);
        Assert.Equal(59_500m, result.GrandTotal);
    }

    [Fact]

    public void Receipt_requires_an_explicit_consistent_purchase_tax_treatment()
    {
        Assert.Throws<ArgumentException>(() => GoodsReceiptCalculator.Calculate([
            (1, Guid.NewGuid(), "Sin IVA", 1m, 1_000m, 0m, "00", 0m,
                PurchaseTaxTreatment.DeductibleInputVat)
        ]));
        Assert.Throws<ArgumentException>(() => GoodsReceiptCalculator.Calculate([
            (1, Guid.NewGuid(), "Con IVA", 1m, 1_000m, 0m, "01", 19m,
                PurchaseTaxTreatment.NotApplicable)
        ]));
    }


    [Theory]
    [InlineData(0, 1_000, 0, 19)]
    [InlineData(1, -1, 0, 19)]
    [InlineData(1, 1_000, -1, 19)]
    [InlineData(1, 1_000, 0, 101)]
    public void Receipt_rejects_invalid_amounts(decimal quantity, decimal cost, decimal discount, decimal tax)
    {
        Assert.ThrowsAny<ArgumentException>(() => GoodsReceiptCalculator.Calculate([
            (1, Guid.NewGuid(), "Producto", quantity, cost, discount, "01", tax,
                PurchaseTaxTreatment.DeductibleInputVat)
        ]));
    }

    [Fact]
    public void Weighted_average_adds_received_value_without_losing_precision()
    {
        var result = WeightedAverageCost.ApplyReceipt(4m, 20_000m, 6m, 7_500m);

        Assert.Equal(10m, result.QuantityAfter);
        Assert.Equal(65_000m, result.InventoryValueAfter);
        Assert.Equal(6_500m, result.AverageUnitCostAfter);
        Assert.Equal(45_000m, result.ReceiptValue);
    }

    [Fact]
    public void Payable_requires_a_valid_due_date_and_preserves_the_opening_balance()
    {
        var received = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(-5));
        var payable = PayableOpening.Create(59_500m, received, received.AddDays(30));

        Assert.Equal(payable.OriginalAmount, payable.OutstandingAmount);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PayableOpening.Create(59_500m, received, received.AddDays(-1)));
    }

    [Fact]
    public void Margin_and_sale_price_use_margin_on_sale_not_markup()
    {
        Assert.Equal(40m, PriceMargin.CalculateMarginPercent(6_000m, 10_000m));
        Assert.Equal(10_000m, PriceMargin.CalculateSalePrice(6_000m, 40m));
        Assert.Equal(12_500m, PriceMargin.SuggestedPricePreservingMargin(6_000m, 10_000m, 7_500m));
    }

    [Theory]
    [InlineData(10_021, 50, "Up", 10_050)]
    [InlineData(10_021, 50, "Down", 10_000)]
    [InlineData(10_026, 50, "Nearest", 10_050)]
    [InlineData(10_024, 50, "Nearest", 10_000)]
    public void Sale_price_rounding_is_deterministic(
        decimal value, decimal increment, string mode, decimal expected)
    {
        Assert.Equal(expected, PriceMargin.RoundPrice(value, increment, mode));
    }

    [Fact]
    public void Sale_price_rounding_rejects_invalid_rules()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceMargin.RoundPrice(100m, 0m, "Up"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceMargin.RoundPrice(100m, 10m, "Other"));
    }
}
