using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class ExternalEscalationAttempt
{
    public Guid ExternalEscalationAttemptId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid SourceAgentId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string ContactKey { get; set; } = string.Empty;
    public string ContactNameSnapshot { get; set; } = string.Empty;
    public string ContactRoleSnapshot { get; set; } = string.Empty;
    public string ContactPhoneSnapshot { get; set; } = string.Empty;
    public Guid InboundAgentIdSnapshot { get; set; }
    public Guid? BusinessInboundContactIdSnapshot { get; set; }
    public string? ContactTypeSnapshot { get; set; }
    public string? PickupAddressSnapshot { get; set; }
    public string AttemptCode { get; set; } = string.Empty;
    public string? CustomPayloadJson { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public ExternalEscalationAttemptStatus Status { get; set; }
    public DateTime EscalatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }
    public DateTime? TimedOutAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? OutcomeKey { get; set; }
    public string? ResponseText { get; set; }
    public string? ResponsePayloadJson { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Agent SourceAgent { get; set; } = null!;
    public virtual Agent InboundAgent { get; set; } = null!;
}
