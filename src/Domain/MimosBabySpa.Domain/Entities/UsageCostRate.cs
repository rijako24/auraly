using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class UsageCostRate
{
    public Guid UsageCostRateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public UsageOperationType OperationType { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal CostUsd { get; set; }
    public decimal CostCop { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
