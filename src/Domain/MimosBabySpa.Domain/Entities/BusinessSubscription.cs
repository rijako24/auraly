using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class BusinessSubscription
{
    public Guid BusinessSubscriptionId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }

    public string PlanCodeSnapshot { get; set; } = string.Empty;
    public string PlanNameSnapshot { get; set; } = string.Empty;
    public decimal MonthlyPriceCop { get; set; }
    public int IncludedCredits { get; set; }
    public decimal MaxVariableCostCop { get; set; }
    public decimal MaxVariableCostPercent { get; set; }
    public int ExtraCredits { get; set; }
    public decimal ExtraVariableCostCop { get; set; }
    public bool AutoRenew { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public ICollection<BusinessUsagePeriod> UsagePeriods { get; set; } = [];
}
