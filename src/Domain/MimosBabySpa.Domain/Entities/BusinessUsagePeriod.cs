using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class BusinessUsagePeriod
{
    public Guid BusinessUsagePeriodId { get; set; }
    public Guid BusinessSubscriptionId { get; set; }
    public Guid BusinessId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int CreditsIncluded { get; set; }
    public int CreditsExtra { get; set; }
    public int CreditsUsed { get; set; }
    public decimal VariableCostLimitCop { get; set; }
    public decimal VariableCostExtraCop { get; set; }
    public decimal VariableCostUsedCop { get; set; }
    public UsagePeriodStatus Status { get; set; } = UsagePeriodStatus.Open;
    public DateTime? ExceededAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public BusinessSubscription BusinessSubscription { get; set; } = null!;
    public Business Business { get; set; } = null!;
    public ICollection<UsageLedgerEntry> LedgerEntries { get; set; } = [];
}
