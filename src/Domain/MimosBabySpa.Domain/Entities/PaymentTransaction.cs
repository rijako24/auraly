using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class PaymentTransaction
{
    public Guid PaymentTransactionId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? ReservationId { get; set; }
    public string PaymentReferenceId { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string? LinkUrl { get; set; }
    public long AmountInCents { get; set; }
    public string Currency { get; set; } = "COP";
    public PaymentTransactionStatus Status { get; set; }
    public PaymentTransactionSource Source { get; set; } = PaymentTransactionSource.Automated;
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public string? WebhookPayloadJson { get; set; }

    public Guid? Snapshot_ServiceId { get; set; }
    public DateTime? Snapshot_ReservationDateTime { get; set; }
    public Guid? Snapshot_PreferredEmployeeId { get; set; }
    public int? Snapshot_DurationMinutes { get; set; }
    public string? Snapshot_CustomerName { get; set; }
    public string? Snapshot_CustomerEmail { get; set; }
    public string? Snapshot_CustomerPhone { get; set; }
    public string? Snapshot_AddOnIds { get; set; }
    public string? Snapshot_CustomAttributesJson { get; set; }
    public bool RequiresRescheduling { get; set; }
    public bool RequiresRefund { get; set; }
    public DateTime? SupersededAt { get; set; }
    public Guid? SupersededByPaymentTransactionId { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Conversation Conversation { get; set; } = null!;
    public virtual Reservation? Reservation { get; set; }
}
