using Auraly.BuildingBlocks.Domain.Money;

namespace Auraly.Application.Sales;

public sealed record FiscalSaleLineAmounts(
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal PromotionDiscountAmount);

public sealed record SaleLineDocumentState(
    decimal Quantity,
    decimal PublicUnitPrice,
    decimal PromotionDiscount,
    decimal DocumentUnitCost,
    decimal TaxRate,
    bool AllowsDocumentCostOverride);

public sealed record SaleLineDocumentUpdate(
    string Description,
    decimal PublicUnitPrice,
    decimal Discount,
    decimal DocumentUnitCost);

public sealed record NormalizedSaleLineDocumentUpdate(
    string Description,
    decimal PublicUnitPrice,
    decimal UntaxedUnitPrice,
    decimal Discount,
    decimal DocumentUnitCost,
    decimal PublicLineTotal,
    bool PriceChanged);

public enum SaleLineDocumentUpdateError
{
    InvalidValues,
    FrozenInventoryCost,
    GenericProductDiscount,
    DiscountExceedsLineValue
}

public sealed record SaleLineDocumentUpdateFailure(
    SaleLineDocumentUpdateError Code,
    string Message);

public sealed record SaleLineDocumentUpdateEvaluation(
    NormalizedSaleLineDocumentUpdate? Update,
    SaleLineDocumentUpdateFailure? Failure)
{
    public bool IsValid => Update is not null;
}

public static class SaleLineMonetaryPolicy
{
    public static SaleLineDocumentUpdateEvaluation EvaluateDocumentUpdate(
        SaleLineDocumentState current,
        SaleLineDocumentUpdate requested)
    {
        if (string.IsNullOrWhiteSpace(requested.Description) ||
            requested.Description.Trim().Length > 250 ||
            requested.PublicUnitPrice < 0 ||
            requested.Discount < 0 ||
            requested.DocumentUnitCost < 0)
            return Invalid(SaleLineDocumentUpdateError.InvalidValues,
                "Cada línea requiere una descripción y valores no negativos.");
        if (requested.DocumentUnitCost != current.DocumentUnitCost &&
            !current.AllowsDocumentCostOverride)
            return Invalid(SaleLineDocumentUpdateError.FrozenInventoryCost,
                "El costo de inventario de la línea queda congelado cuando se agrega el producto.");
        if (current.AllowsDocumentCostOverride && requested.Discount != 0)
            return Invalid(SaleLineDocumentUpdateError.GenericProductDiscount,
                "Un producto genérico siempre tiene descuento cero.");

        var publicUnitPrice = MonetaryRounding.RoundLineAmount(requested.PublicUnitPrice);
        var discount = MonetaryRounding.RoundLineAmount(requested.Discount);
        if (discount > current.Quantity * publicUnitPrice - current.PromotionDiscount)
            return Invalid(SaleLineDocumentUpdateError.DiscountExceedsLineValue,
                "El descuento no puede superar el valor de la línea.");

        return new(
            new(
                requested.Description.Trim(),
                publicUnitPrice,
                MonetaryRounding.RoundLineAmount(TaxExclusive(publicUnitPrice, current.TaxRate)),
                discount,
                requested.DocumentUnitCost,
                MonetaryRounding.RoundLineAmount(
                    current.Quantity * publicUnitPrice - discount - current.PromotionDiscount),
                publicUnitPrice != current.PublicUnitPrice),
            null);
    }

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

    private static SaleLineDocumentUpdateEvaluation Invalid(
        SaleLineDocumentUpdateError code,
        string message) =>
        new(null, new(code, message));

    private static decimal TaxExclusive(decimal amount, decimal taxRate) =>
        decimal.Round(
            amount / (1m + taxRate / 100m),
            6,
            MidpointRounding.AwayFromZero);
}
