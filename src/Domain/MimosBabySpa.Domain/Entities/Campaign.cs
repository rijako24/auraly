namespace MimosBabySpa.Domain.Entities;

public class Campaign
{
    public Guid CampaignId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string SourceType { get; set; } = "Segment";
    public string? FiltersJson { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "es_CO";
    public string TemplateCategory { get; set; } = "Marketing";
    public string? ParameterMappingJson { get; set; }
    public DateTime? ScheduledAtUtc { get; set; }
    public int RecipientCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual AppUser CreatedByUser { get; set; } = null!;
    public virtual ICollection<CampaignRecipient> Recipients { get; set; } = new List<CampaignRecipient>();
}
