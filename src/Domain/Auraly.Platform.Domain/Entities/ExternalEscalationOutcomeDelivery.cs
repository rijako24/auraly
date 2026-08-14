namespace Auraly.Platform.Domain.Entities;

public class ExternalEscalationOutcomeDelivery
{
    public Guid ExternalEscalationOutcomeDeliveryId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ExternalEscalationAttemptId { get; set; }
    public string OutcomeKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int PublishAttempts { get; set; }
    public string? LastError { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ExternalEscalationAttempt Attempt { get; set; } = null!;
}
