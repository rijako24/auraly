using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Billing;

public sealed class UsageBillingService : IUsageBillingService
{
    private const decimal DefaultGpt4oMiniInputCostCopPerToken = 0.00054m;
    private const decimal DefaultGpt4oMiniOutputCostCopPerToken = 0.00216m;
    private const decimal DefaultOperationCostCop = 0.25m;
    private const decimal DefaultOutboundSessionCostCop = 0.05m;
    private const decimal DefaultSafetyBufferPercent = 0.20m;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UsageBillingService> _logger;

    public UsageBillingService(IUnitOfWork unitOfWork, ILogger<UsageBillingService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<UsageGateResult> CanProcessAsync(Guid businessId, CancellationToken ct = default)
    {
        var period = await ResolveCurrentPeriodAsync(businessId, createIfMissing: true, ct);
        if (period is null)
            return new UsageGateResult(false, "subscription_inactive", "No active subscription.", null);

        var snapshot = BuildSnapshot(period);
        if (period.Status != UsagePeriodStatus.Open)
            return new UsageGateResult(false, "usage_period_closed", "Usage period is not open.", snapshot);

        if (period.CreditsUsed >= period.CreditsIncluded + period.CreditsExtra)
            return await MarkExceededAsync(period, "credits_exceeded", ct);

        if (period.VariableCostUsedCop >= period.VariableCostLimitCop + period.VariableCostExtraCop)
            return await MarkExceededAsync(period, "variable_cost_exceeded", ct);

        return new UsageGateResult(true, "ok", "Allowed.", snapshot);
    }

    public async Task<UsageChargeResult> ChargeAsync(UsageChargeRequest request, CancellationToken ct = default)
    {
        var period = await ResolveCurrentPeriodAsync(request.BusinessId, createIfMissing: true, ct);
        if (period is null)
        {
            _logger.LogWarning("Usage charge skipped: no active subscription for BusinessId={BusinessId}", request.BusinessId);
            return new UsageChargeResult(false, 0, 0, null);
        }

        var estimatedCost = EstimateCostCop(request);
        var creditValue = CalculateInternalCreditValue(period);
        var credits = Math.Max(GetMinimumCredits(request), (int)Math.Ceiling(estimatedCost / creditValue));

        await _unitOfWork.UsageLedger.AddAsync(new UsageLedgerEntry
        {
            UsageLedgerEntryId = Guid.NewGuid(),
            BusinessUsagePeriodId = period.BusinessUsagePeriodId,
            BusinessId = request.BusinessId,
            AgentId = request.AgentId,
            ConversationId = request.ConversationId,
            MessageId = request.MessageId,
            OperationType = request.OperationType,
            CreditsCharged = credits,
            EstimatedCostCop = estimatedCost,
            InputTokens = request.InputTokens,
            OutputTokens = request.OutputTokens,
            Model = request.Model,
            MetadataJson = request.MetadataJson,
            CreatedAt = DateTime.UtcNow
        }, ct);

        period.CreditsUsed += credits;
        period.VariableCostUsedCop += estimatedCost;
        period.UpdatedAt = DateTime.UtcNow;

        if (period.CreditsUsed >= period.CreditsIncluded + period.CreditsExtra
            || period.VariableCostUsedCop >= period.VariableCostLimitCop + period.VariableCostExtraCop)
        {
            period.Status = UsagePeriodStatus.Exceeded;
            period.ExceededAt ??= DateTime.UtcNow;
        }

        await _unitOfWork.BusinessUsagePeriods.UpdateAsync(period, ct);
        return new UsageChargeResult(true, credits, estimatedCost, BuildSnapshot(period));
    }

    public async Task<BusinessUsageSnapshot?> GetCurrentUsageAsync(Guid businessId, CancellationToken ct = default)
    {
        var period = await ResolveCurrentPeriodAsync(businessId, createIfMissing: true, ct);
        return period is null ? null : BuildSnapshot(period);
    }

    public async Task<IReadOnlyList<UsagePlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        var plans = await _unitOfWork.SubscriptionPlans.GetActiveAsync(ct);
        return plans.Select(p => new UsagePlanDto(
            p.Code,
            p.Name,
            p.MonthlyPriceCop,
            p.IncludedCredits,
            p.MaxVariableCostCop,
            p.MaxVariableCostPercent,
            p.IncludedAgents,
            p.IncludedUsers,
            p.IncludedWorkspaces,
            ParseFeatures(p.FeaturesJson))).ToList();
    }

    private async Task<UsageGateResult> MarkExceededAsync(BusinessUsagePeriod period, string code, CancellationToken ct)
    {
        period.Status = UsagePeriodStatus.Exceeded;
        period.ExceededAt ??= DateTime.UtcNow;
        await _unitOfWork.BusinessUsagePeriods.UpdateAsync(period, ct);
        return new UsageGateResult(false, code, "Usage limit exceeded.", BuildSnapshot(period));
    }

    private async Task<BusinessUsagePeriod?> ResolveCurrentPeriodAsync(Guid businessId, bool createIfMissing, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var subscription = await _unitOfWork.BusinessSubscriptions.GetActiveByBusinessIdAsync(businessId, ct);
        if (subscription is null)
            return null;

        var period = await _unitOfWork.BusinessUsagePeriods.GetCurrentAsync(subscription.BusinessSubscriptionId, now, ct);
        if (period is not null || !createIfMissing)
            return period;

        return await _unitOfWork.BusinessUsagePeriods.AddAsync(new BusinessUsagePeriod
        {
            BusinessUsagePeriodId = Guid.NewGuid(),
            BusinessSubscriptionId = subscription.BusinessSubscriptionId,
            BusinessId = subscription.BusinessId,
            PeriodStart = subscription.CurrentPeriodStart,
            PeriodEnd = subscription.CurrentPeriodEnd,
            CreditsIncluded = subscription.IncludedCredits,
            CreditsExtra = subscription.ExtraCredits,
            VariableCostLimitCop = subscription.MaxVariableCostCop,
            VariableCostExtraCop = subscription.ExtraVariableCostCop,
            Status = UsagePeriodStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);
    }

    private decimal EstimateCostCop(UsageChargeRequest request)
    {
        var inputCost = request.InputTokens * DefaultGpt4oMiniInputCostCopPerToken;
        var outputCost = request.OutputTokens * DefaultGpt4oMiniOutputCostCopPerToken;
        var operationCost = request.OperationCalls * DefaultOperationCostCop;
        var outboundCost = request.OutboundMessages * DefaultOutboundSessionCostCop;
        var subtotal = inputCost + outputCost + operationCost + outboundCost + request.AdditionalCostCop;
        return Math.Round(subtotal * (1 + DefaultSafetyBufferPercent), 4);
    }

    private static int GetMinimumCredits(UsageChargeRequest request) =>
        request.OperationType switch
        {
            UsageOperationType.AgentTurn => string.IsNullOrWhiteSpace(request.Model) ? 1 : 1,
            UsageOperationType.AudioTranscription => 5,
            UsageOperationType.WhatsappUtilityTemplate => 1,
            UsageOperationType.WhatsappMarketingTemplate => 10,
            UsageOperationType.OutboundSequence => Math.Max(1, request.OutboundMessages),
            _ => 1
        };

    private static decimal CalculateInternalCreditValue(BusinessUsagePeriod period)
    {
        var credits = Math.Max(1, period.CreditsIncluded + period.CreditsExtra);
        var limit = Math.Max(1, period.VariableCostLimitCop + period.VariableCostExtraCop);
        return limit / credits;
    }

    private static BusinessUsageSnapshot BuildSnapshot(BusinessUsagePeriod period)
    {
        var subscription = period.BusinessSubscription;
        var creditsLimit = Math.Max(1, period.CreditsIncluded + period.CreditsExtra);
        var variableLimit = Math.Max(1, period.VariableCostLimitCop + period.VariableCostExtraCop);

        return new BusinessUsageSnapshot(
            subscription.PlanNameSnapshot,
            subscription.PlanCodeSnapshot,
            creditsLimit,
            period.CreditsUsed,
            Math.Round(period.CreditsUsed / (decimal)creditsLimit * 100, 2),
            variableLimit,
            period.VariableCostUsedCop,
            Math.Round(period.VariableCostUsedCop / variableLimit * 100, 2),
            period.PeriodStart,
            period.PeriodEnd,
            period.Status);
    }

    private static string[] ParseFeatures(string featuresJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(featuresJson) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
