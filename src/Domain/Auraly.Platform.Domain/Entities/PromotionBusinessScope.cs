namespace Auraly.Platform.Domain.Entities;

public sealed class PromotionBusinessScope
{
    public Guid PromotionId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid TenantId { get; set; }

    public Promotion Promotion { get; set; } = null!;
    public Business Business { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}
