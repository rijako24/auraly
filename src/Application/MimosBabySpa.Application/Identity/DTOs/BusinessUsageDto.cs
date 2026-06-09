using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

public record BusinessUsageDto(
    string PlanName,
    string PlanCode,
    int CreditsLimit,
    int CreditsUsed,
    decimal CreditsUsagePercent,
    decimal VariableCostLimitCop,
    decimal VariableCostUsedCop,
    decimal VariableCostUsagePercent,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    UsagePeriodStatus Status);

public record SubscriptionPlanDto(
    string Code,
    string Name,
    decimal MonthlyPriceCop,
    int IncludedCredits,
    decimal MaxVariableCostCop,
    decimal MaxVariableCostPercent,
    int IncludedAgents,
    int IncludedUsers,
    int IncludedWorkspaces,
    string[] Features);
