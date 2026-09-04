namespace Auraly.Platform.Domain.Entities;

public class Promotion
{
    public Guid PromotionId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public int Priority { get; set; }
    public bool IsCombinable { get; set; }
    public bool AppliesToAllBusinesses { get; set; }
    public string? CouponCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Tenant Tenant { get; set; } = null!;
    public virtual ICollection<PromotionBusinessScope> BusinessScopes { get; set; } = new List<PromotionBusinessScope>();
    public virtual ICollection<PromotionCondition> Conditions { get; set; } = new List<PromotionCondition>();
    public virtual ICollection<PromotionBenefit> Benefits { get; set; } = new List<PromotionBenefit>();
}
