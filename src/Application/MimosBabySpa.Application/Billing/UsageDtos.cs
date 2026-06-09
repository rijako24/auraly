using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Billing;

public sealed record UsageGateResult(
    bool IsAllowed,
    string Code,
    string Reason,
    BusinessUsageSnapshot? Snapshot);

public sealed record BusinessUsageSnapshot(
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

public sealed record UsageChargeRequest(
    Guid BusinessId,
    Guid? AgentId,
    Guid? ConversationId,
    Guid? MessageId,
    UsageOperationType OperationType,
    int InputTokens = 0,
    int OutputTokens = 0,
    int ToolCalls = 0,
    int OutboundMessages = 0,
    string Model = "",
    decimal AdditionalCostCop = 0,
    string? MetadataJson = null);

public sealed record UsageChargeResult(
    bool Charged,
    int CreditsCharged,
    decimal EstimatedCostCop,
    BusinessUsageSnapshot? Snapshot);

public sealed record UsagePlanDto(
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
