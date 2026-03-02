using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Transacción de pago para idempotencia del webhook y auditoría.
/// </summary>
public class PaymentTransaction
{
    public Guid PaymentTransactionId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ConversationId { get; set; }
    public string PaymentReferenceId { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public long AmountInCents { get; set; }
    public string Currency { get; set; } = "COP";
    public PaymentTransactionStatus Status { get; set; }
    public PaymentTransactionSource Source { get; set; } = PaymentTransactionSource.Automated;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public string? WebhookPayloadJson { get; set; }

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual Conversation Conversation { get; set; } = null!;
}
