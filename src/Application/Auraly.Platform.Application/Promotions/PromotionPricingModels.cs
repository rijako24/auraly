using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Promotions;

public sealed record PromotionPricingItem(
    string Key,
    PromotionItemType ItemType,
    Guid? ProductId,
    Guid? ServiceId,
    string Name,
    string? CategoryName,
    decimal UnitPrice,
    decimal Quantity,
    bool IncludeInTotal = true);

public sealed record PromotionAppliedAdjustment(
    Guid PromotionId,
    string PromotionName,
    decimal DiscountAmount,
    string Summary);

public sealed record PromotionPricedItem(
    PromotionPricingItem Item,
    decimal LineSubtotal,
    decimal DiscountAmount,
    decimal LineTotal,
    decimal EffectiveUnitPrice,
    IReadOnlyList<PromotionAppliedAdjustment> Adjustments)
{
    public bool HasPromotion => DiscountAmount > 0;
    public string? PromotionName => Adjustments.FirstOrDefault()?.PromotionName;
    public string? PromotionSummary => Adjustments.FirstOrDefault()?.Summary;
}

public sealed record PromotionPricingResult(
    IReadOnlyList<PromotionPricedItem> Items,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Total)
{
    public static PromotionPricingResult Empty(IReadOnlyList<PromotionPricingItem> items)
    {
        var priced = items.Select(item =>
        {
            var subtotal = item.UnitPrice * item.Quantity;
            return new PromotionPricedItem(item, subtotal, 0, subtotal, item.UnitPrice, []);
        }).ToList();

        var subtotal = priced.Where(i => i.Item.IncludeInTotal).Sum(i => i.LineSubtotal);
        return new PromotionPricingResult(priced, subtotal, 0, subtotal);
    }
}

public sealed record PromotionPreview(
    decimal BaseUnitPrice,
    decimal EffectiveUnitPrice,
    decimal DiscountAmount,
    Guid? PromotionId,
    string? PromotionName,
    string? PromotionSummary,
    bool HasPromotion)
{
    public static PromotionPreview None(decimal baseUnitPrice) =>
        new(baseUnitPrice, baseUnitPrice, 0, null, null, null, false);
}
