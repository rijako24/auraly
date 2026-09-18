using Auraly.BuildingBlocks.Domain.Money;

namespace Auraly.Application.Sales;

public sealed record FiscalSaleLineAmounts(
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal PromotionDiscountAmount);

public static class SaleLineMonetaryPolicy
{
    public static FiscalSaleLineAmounts FromClosedPublishedAmounts(
        decimal quantity,
        decimal untaxedAmount,
        decimal discountAmount,
        decimal promotionDiscountAmount,
        decimal taxRate)
    {
        if (quantity <= 0 || untaxedAmount < 0 || discountAmount < 0 ||
            promotionDiscountAmount < 0 || taxRate < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (untaxedAmount != MonetaryRounding.RoundLineAmount(untaxedAmount))
            throw new ArgumentException(
                "The untaxed line amount must already be closed to monetary precision.",
                nameof(untaxedAmount));

        var manualDiscount = MonetaryRounding.RoundLineAmount(
            TaxExclusive(discountAmount, taxRate));
        var promotionDiscount = MonetaryRounding.RoundLineAmount(
            TaxExclusive(promotionDiscountAmount, taxRate));
        var totalDiscount = manualDiscount + promotionDiscount;
        var unitPrice = decimal.Round(
            (untaxedAmount + totalDiscount) / quantity,
            4,
            MidpointRounding.ToEven);
        return new(unitPrice, totalDiscount, promotionDiscount);
    }

    private static decimal TaxExclusive(decimal amount, decimal taxRate) =>
        decimal.Round(
            amount / (1m + taxRate / 100m),
            6,
            MidpointRounding.AwayFromZero);
}
