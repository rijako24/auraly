using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

public record BusinessUsageDto(
    string PlanName,
    string PlanCode,
    int CreditsLimit,
    int CreditsUsed,
    decimal CreditsUsagePercent,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    UsagePeriodStatus Status);

public record SubscriptionPlanDto(
    string Code,
    string Name,
    decimal MonthlyPriceCop,
    int IncludedCredits,
    int IncludedAgents,
    int IncludedUsers,
    int IncludedWorkspaces,
    string[] Features);

public record SubscriptionDetailsDto(
    Guid SubscriptionId,
    string PlanName,
    string PlanCode,
    decimal MonthlyPriceCop,
    DateTime SubscriptionStartedAt,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    bool AutoRenew,
    SubscriptionStatus Status,
    UsagePeriodStatus UsageStatus,
    int CreditsIncluded,
    int CreditsExtra,
    int CreditsLimit,
    int CreditsUsed,
    int CreditsRemaining,
    decimal CreditsUsagePercent,
    int IncludedAgents,
    int IncludedUsers,
    int IncludedWorkspaces,
    string[] Features,
    IReadOnlyList<UsageBreakdownDto> UsageBreakdown,
    IReadOnlyList<UsageActivityDto> RecentUsage);

public record UsageBreakdownDto(
    UsageOperationType OperationType,
    int OperationCount,
    int CreditsUsed,
    decimal CreditsPercent);

public record UsageActivityDto(
    Guid UsageId,
    UsageOperationType OperationType,
    int CreditsUsed,
    DateTime CreatedAt);
