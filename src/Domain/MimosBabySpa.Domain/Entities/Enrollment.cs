using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Enrollment
{
    public Guid EnrollmentId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid PaymentTransactionId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? FixedScheduleLabel { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Paid;
    public string? CustomAttributesJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business Business { get; set; } = null!;
    public virtual Conversation Conversation { get; set; } = null!;
    public virtual Service Service { get; set; } = null!;
    public virtual PaymentTransaction PaymentTransaction { get; set; } = null!;
}
