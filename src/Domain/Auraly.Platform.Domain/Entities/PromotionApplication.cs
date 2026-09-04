namespace Auraly.Platform.Domain.Entities;

public class PromotionApplication
{
    public Guid PromotionApplicationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? ReservationId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public decimal DiscountAmount { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual Tenant Tenant { get; set; } = null!;
    public virtual Business Business { get; set; } = null!;
    public virtual Promotion Promotion { get; set; } = null!;
    public virtual Order? Order { get; set; }
    public virtual Reservation? Reservation { get; set; }
    public virtual PaymentTransaction? PaymentTransaction { get; set; }
}
