namespace MimosBabySpa.Domain.Entities;

public class Promotion
{
    public Guid PromotionId { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public int Priority { get; set; }
    public bool IsCombinable { get; set; }
    public string? CouponCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<PromotionCondition> Conditions { get; set; } = new List<PromotionCondition>();
    public virtual ICollection<PromotionBenefit> Benefits { get; set; } = new List<PromotionBenefit>();
}
