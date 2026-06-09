using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class UsageLedgerEntry
{
    public Guid UsageLedgerEntryId { get; set; }
    public Guid BusinessUsagePeriodId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public UsageOperationType OperationType { get; set; }
    public int CreditsCharged { get; set; }
    public decimal EstimatedCostCop { get; set; }
    public decimal? ActualCostCop { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BusinessUsagePeriod BusinessUsagePeriod { get; set; } = null!;
    public Business Business { get; set; } = null!;
    public Agent? Agent { get; set; }
    public Conversation? Conversation { get; set; }
}
