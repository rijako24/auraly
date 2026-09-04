using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class PromotionBenefit
{
    public Guid PromotionBenefitId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid TenantId { get; set; }
    public PromotionBenefitType BenefitType { get; set; }
    public PromotionItemType TargetItemType { get; set; } = PromotionItemType.Any;
    public Guid? ProductId { get; set; }
    public Guid? ServiceId { get; set; }
    public string? CategoryName { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? FixedUnitPrice { get; set; }
    public decimal? AppliesToQuantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Promotion Promotion { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual Product? Product { get; set; }
    public virtual Service? Service { get; set; }
}
