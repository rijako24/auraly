using Auraly.Application.Sales;

namespace Auraly.Foundation.Tests;

public sealed class OnlineSalesOrderCheckoutLineMapperTests
{
    [Fact]
    public void Normalize_PreservesThePublicTotalAndStoredCost()
    {
        var line = CreateLine(
            quantity: 1m,
            publicUnitPrice: 12_500m,
            discount: 0m,
            publicLineTotal: 12_500m,
            taxRate: 19m,
            cost: 8_125.456789m) with { Description = "Nombre editado en caja" };

        var normalized = Assert.Single(
            OnlineSalesOrderCheckoutLineMapper.Normalize([line]));

        Assert.Equal(10_504.20m, normalized.Net);
        Assert.Equal(1_995.80m, normalized.Tax);
        Assert.Equal(12_500m, normalized.Total);
        Assert.Equal(8_125.456789m, normalized.DocumentUnitCost);
        Assert.Equal("Nombre editado en caja", normalized.Description);
    }

    [Theory]
    [InlineData("Captured", 500, 0)]
    [InlineData("Promotion", 0, 500)]
    public void Normalize_KeepsTheSingleDiscountOwner(
        string priceSource,
        decimal expectedDiscount,
        decimal expectedPromotion)
    {
        var line = CreateLine(
            quantity: 2m,
            publicUnitPrice: 3_980m,
            discount: 500m,
            publicLineTotal: 7_460m,
            taxRate: 19m,
            cost: 2_793m) with { PriceSource = priceSource };

        var normalized = Assert.Single(
            OnlineSalesOrderCheckoutLineMapper.Normalize([line]));

        Assert.Equal(7_460m, normalized.Total);
        Assert.Equal(expectedDiscount, normalized.Discount);
        Assert.Equal(expectedPromotion, normalized.PromotionDiscount);
    }

    [Fact]
    public void Normalize_PreservesTheClosedLineTotalAtAMonetaryMidpoint()
    {
        var line = CreateLine(
            quantity: 0.3m,
            publicUnitPrice: 15_340.45m,
            discount: 0m,
            publicLineTotal: 4_602.13m,
            taxRate: 0m,
            cost: 10_000m);

        var normalized = Assert.Single(
            OnlineSalesOrderCheckoutLineMapper.Normalize([line]));
        var fiscal = SaleLineMonetaryPolicy.FromClosedPublishedAmounts(
            normalized.Quantity,
            normalized.Net,
            normalized.Discount,
            normalized.PromotionDiscount,
            normalized.TaxRate);

        Assert.Equal(4_602.13m, normalized.Net);
        Assert.Equal(4_602.13m, normalized.Total);
        Assert.Equal(0m, fiscal.DiscountAmount);
        Assert.Equal(15_340.4333m, fiscal.UnitPrice);
    }

    [Fact]
    public void FiscalProjection_UsesOnlyRealDiscountsAndDerivesItsUnitPriceFromTheClosedAmount()
    {
        var fiscal = SaleLineMonetaryPolicy.FromClosedPublishedAmounts(
            quantity: 2m,
            untaxedAmount: 20_000m,
            discountAmount: 1_190m,
            promotionDiscountAmount: 595m,
            taxRate: 19m);

        Assert.Equal(10_750m, fiscal.UnitPrice);
        Assert.Equal(1_500m, fiscal.DiscountAmount);
        Assert.Equal(500m, fiscal.PromotionDiscountAmount);
    }

    [Fact]
    public void DocumentUpdate_KeepsAutomaticPriceOwnershipWhenOnlyTheDiscountChanges()
    {
        var evaluation = SaleLineMonetaryPolicy.EvaluateDocumentUpdate(
            new(
                Quantity: 2m,
                PublicUnitPrice: 10_000m,
                PromotionDiscount: 500m,
                DocumentUnitCost: 7_000m,
                TaxRate: 19m,
                AllowsDocumentCostOverride: false),
            new(
                Description: " Producto ",
                PublicUnitPrice: 10_000m,
                Discount: 1_000m,
                DocumentUnitCost: 7_000m));

        Assert.True(evaluation.IsValid);
        Assert.False(evaluation.Update!.PriceChanged);
        Assert.Equal("Producto", evaluation.Update.Description);
        Assert.Equal(18_500m, evaluation.Update.PublicLineTotal);
        Assert.Equal(8_403.36m, evaluation.Update.UntaxedUnitPrice);
    }

    [Fact]
    public void DocumentUpdate_UsesTheSameDiscountLimitForEveryPersistenceEngine()
    {
        var evaluation = SaleLineMonetaryPolicy.EvaluateDocumentUpdate(
            new(
                Quantity: 1m,
                PublicUnitPrice: 10_000m,
                PromotionDiscount: 1_000m,
                DocumentUnitCost: 7_000m,
                TaxRate: 0m,
                AllowsDocumentCostOverride: false),
            new(
                Description: "Producto",
                PublicUnitPrice: 10_000m,
                Discount: 9_001m,
                DocumentUnitCost: 7_000m));

        Assert.False(evaluation.IsValid);
        Assert.Equal(
            SaleLineDocumentUpdateError.DiscountExceedsLineValue,
            evaluation.Failure!.Code);
        Assert.Equal(
            "El descuento no puede superar el valor de la línea.",
            evaluation.Failure.Message);
    }

    private static OnlineSalesOrderCheckoutLine CreateLine(
        decimal quantity,
        decimal publicUnitPrice,
        decimal discount,
        decimal publicLineTotal,
        decimal taxRate,
        decimal cost) => new(
            Guid.NewGuid(), Guid.NewGuid(), "P-01", "Producto", "EA", "01",
            taxRate, quantity, publicUnitPrice, discount, publicLineTotal, cost,
            "COP", "Captured");
}
