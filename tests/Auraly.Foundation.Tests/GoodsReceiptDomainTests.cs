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
        var result = InventoryValuationCalculator.Calculate(
            State(4m, 6_500m, 20_000m), 6m, 7_500m,
            InventoryValuationMode.WeightedAverageReceipt);

        Assert.Equal(10m, result.QuantityAfter);
        Assert.Equal(65_000m, result.InventoryValueAfter);
        Assert.Equal(6_500m, result.AverageUnitCostAfter);
        Assert.Equal(45_000m, result.ValueChange);
    }

    [Fact]
    public void Weighted_average_preserves_negative_stock_and_does_not_corrupt_cost()
    {
        var result = InventoryValuationCalculator.Calculate(
            State(-10m, 5_000m, -50_000m), 4m, 6_000m,
            InventoryValuationMode.WeightedAverageReceipt);

        Assert.Equal(-6m, result.QuantityAfter);
        Assert.Equal(5_000m, result.AverageUnitCostAfter);
        Assert.Equal(-30_000m, result.InventoryValueAfter);
        Assert.Equal(24_000m, result.ValueChange);
    }

    [Fact]
    public void Receipt_crossing_negative_stock_starts_positive_inventory_at_receipt_cost()
    {
        var result = InventoryValuationCalculator.Calculate(
            State(-10m, 5_000m, -50_000m), 14m, 6_000m,
            InventoryValuationMode.WeightedAverageReceipt);

        Assert.Equal(4m, result.QuantityAfter);
        Assert.Equal(6_000m, result.AverageUnitCostAfter);
        Assert.Equal(24_000m, result.InventoryValueAfter);
        Assert.Equal(84_000m, result.ValueChange);
    }

    [Fact]
    public void Repeated_receipts_do_not_change_average_while_stock_remains_negative()
    {
        var first = InventoryValuationCalculator.Calculate(
            State(-10m, 5_000m, -50_000m), 4m, 6_000m,
            InventoryValuationMode.WeightedAverageReceipt);
        var second = InventoryValuationCalculator.Calculate(
            State(first.QuantityAfter, first.AverageUnitCostAfter, first.InventoryValueAfter),
            3m, 8_000m, InventoryValuationMode.WeightedAverageReceipt);

        Assert.Equal(-3m, second.QuantityAfter);
        Assert.Equal(5_000m, second.AverageUnitCostAfter);
        Assert.Equal(-15_000m, second.InventoryValueAfter);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(-15, -5)]
    public void Average_cost_issue_never_erases_last_cost_at_or_below_zero(
        decimal quantityChange,
        decimal expectedQuantity)
    {
        var result = InventoryValuationCalculator.Calculate(
            State(10m, 5_000m, 50_000m), quantityChange, null,
            InventoryValuationMode.AverageCost);

        Assert.Equal(expectedQuantity, result.QuantityAfter);
        Assert.Equal(5_000m, result.AverageUnitCostAfter);
        Assert.Equal(expectedQuantity * 5_000m, result.InventoryValueAfter);
        Assert.Equal(quantityChange * 5_000m, result.ValueChange);
    }

    [Fact]
    public void Sequential_sales_keep_the_last_average_through_zero_and_negative_stock()
    {
        var state = State(3m, 5_000m, 15_000m);
        foreach (var expectedQuantity in new[] { 2m, 1m, 0m, -1m })
        {
            var sale = InventoryValuationCalculator.Calculate(
                state, -1m, null, InventoryValuationMode.AverageCost);

            Assert.Equal(expectedQuantity, sale.QuantityAfter);
            Assert.Equal(5_000m, sale.AverageUnitCostAfter);
            Assert.Equal(expectedQuantity * 5_000m, sale.InventoryValueAfter);
            state = State(
                sale.QuantityAfter,
                sale.AverageUnitCostAfter,
                sale.InventoryValueAfter);
        }

        var partialReceipt = InventoryValuationCalculator.Calculate(
            state, 0.5m, 7_000m, InventoryValuationMode.WeightedAverageReceipt);
        Assert.Equal(-0.5m, partialReceipt.QuantityAfter);
        Assert.Equal(5_000m, partialReceipt.AverageUnitCostAfter);

        var positiveReceipt = InventoryValuationCalculator.Calculate(
            State(
                partialReceipt.QuantityAfter,
                partialReceipt.AverageUnitCostAfter,
                partialReceipt.InventoryValueAfter),
            1m, 9_000m, InventoryValuationMode.WeightedAverageReceipt);
        Assert.Equal(0.5m, positiveReceipt.QuantityAfter);
        Assert.Equal(9_000m, positiveReceipt.AverageUnitCostAfter);
        Assert.Equal(4_500m, positiveReceipt.InventoryValueAfter);
    }

    [Fact]
    public void Receipt_after_stock_reached_zero_uses_the_new_real_cost()
    {
        var issue = InventoryValuationCalculator.Calculate(
            State(10m, 5_000m, 50_000m), -10m, null,
            InventoryValuationMode.AverageCost);
        var receipt = InventoryValuationCalculator.Calculate(
            State(issue.QuantityAfter, issue.AverageUnitCostAfter, issue.InventoryValueAfter),
            4m, 6_500m, InventoryValuationMode.WeightedAverageReceipt);

        Assert.Equal(4m, receipt.QuantityAfter);
        Assert.Equal(6_500m, receipt.AverageUnitCostAfter);
        Assert.Equal(26_000m, receipt.InventoryValueAfter);
    }

    [Fact]
    public void Specified_cost_issue_preserves_last_cost_when_it_empties_the_balance()
    {
        var result = InventoryValuationCalculator.Calculate(
            State(3m, 6_000m, 18_000m), -3m, 6_000m,
            InventoryValuationMode.SpecifiedCostIssue);

        Assert.Equal(0m, result.QuantityAfter);
        Assert.Equal(6_000m, result.AverageUnitCostAfter);
        Assert.Equal(0m, result.InventoryValueAfter);
        Assert.Equal(-18_000m, result.ValueChange);
    }

    [Fact]
    public void Valuation_uses_the_whole_business_pool_not_one_warehouse()
    {
        var issue = InventoryValuationCalculator.Calculate(
            new InventoryValuationState(
                QuantityOnHand: 5m,
                AverageUnitCost: 4_000m,
                PoolQuantityOnHand: 10m,
                PoolInventoryValue: 60_000m),
            -2m,
            null,
            InventoryValuationMode.AverageCost);
        var returnToSupplier = InventoryValuationCalculator.Calculate(
            new InventoryValuationState(
                QuantityOnHand: 5m,
                AverageUnitCost: 6_000m,
                PoolQuantityOnHand: 10m,
                PoolInventoryValue: 60_000m),
            -2m,
            5_000m,
            InventoryValuationMode.SpecifiedCostIssue);

        Assert.Equal(6_000m, issue.AverageUnitCostBefore);
        Assert.Equal(6_000m, issue.RecognizedUnitCost);
        Assert.Equal(6_250m, returnToSupplier.AverageUnitCostAfter);
        Assert.Equal(18_750m, returnToSupplier.InventoryValueAfter);
    }

    private static InventoryValuationState State(
        decimal quantity,
        decimal average,
        decimal value) =>
        new(quantity, average, quantity, value);

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
