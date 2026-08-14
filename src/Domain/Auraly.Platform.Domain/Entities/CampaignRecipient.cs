namespace Auraly.Platform.Domain.Entities;

public class CampaignRecipient
{
    public Guid CampaignRecipientId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid BusinessId { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public Guid? SourceLeadId { get; set; }
    public Guid? SourceReservationId { get; set; }
    public string Status { get; set; } = "Pending";
    public string? WhatsAppMessageId { get; set; }
    public string? Error { get; set; }
    public string? VariablesJson { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAt { get; set; }

    public virtual Campaign Campaign { get; set; } = null!;
    public virtual Business Business { get; set; } = null!;
    public virtual Lead? SourceLead { get; set; }
    public virtual Reservation? SourceReservation { get; set; }
}
