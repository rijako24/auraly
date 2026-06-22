namespace MimosBabySpa.Application.Promotions;

public interface IPromotionPricingService
{
    Task<PromotionPricingResult> EvaluateAsync(
        Guid businessId,
        IReadOnlyList<PromotionPricingItem> items,
        DateTime? utcNow = null,
        CancellationToken ct = default);

    Task<PromotionPreview> PreviewAsync(
        Guid businessId,
        PromotionPricingItem item,
        DateTime? utcNow = null,
        CancellationToken ct = default);
}
