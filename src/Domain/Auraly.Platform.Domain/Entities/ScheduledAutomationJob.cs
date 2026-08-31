using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class ScheduledAutomationJob
{
    public Guid ScheduledAutomationJobId { get; set; }
    public Guid? BusinessId { get; set; }
    public Guid? ReservationId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? TenantSubscriptionId { get; set; }
    public ScheduledAutomationJobType JobType { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public ScheduledAutomationJobStatus Status { get; set; } = ScheduledAutomationJobStatus.Pending;
    public string DeduplicationKey { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? WhatsAppMessageId { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business? Business { get; set; }
    public virtual Reservation? Reservation { get; set; }
    public virtual Agent? Agent { get; set; }
}
