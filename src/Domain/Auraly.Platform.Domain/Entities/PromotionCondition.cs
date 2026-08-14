using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class PromotionCondition
{
    public Guid PromotionConditionId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid BusinessId { get; set; }
    public PromotionItemType ItemType { get; set; } = PromotionItemType.Any;
    public Guid? ProductId { get; set; }
    public Guid? ServiceId { get; set; }
    public string? CategoryName { get; set; }
    public decimal MinQuantity { get; set; } = 1;
    public decimal? MinSubtotal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Promotion Promotion { get; set; } = null!;
    public virtual Business Business { get; set; } = null!;
    public virtual Product? Product { get; set; }
    public virtual Service? Service { get; set; }
}
