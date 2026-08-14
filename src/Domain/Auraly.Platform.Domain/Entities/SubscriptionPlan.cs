using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

public class SubscriptionPlan
{
    public Guid SubscriptionPlanId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPriceCop { get; set; }
    public int IncludedCredits { get; set; }
    public decimal MaxVariableCostCop { get; set; }
    public decimal MaxVariableCostPercent { get; set; }
    public int IncludedAgents { get; set; }
    public int IncludedUsers { get; set; }
    public int IncludedWorkspaces { get; set; }
    public string FeaturesJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BusinessSubscription> BusinessSubscriptions { get; set; } = [];
}
