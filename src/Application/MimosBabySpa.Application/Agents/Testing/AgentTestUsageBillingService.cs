using MimosBabySpa.Application.Billing;

namespace MimosBabySpa.Application.Agents.Testing;

internal sealed class AgentTestUsageBillingService : IUsageBillingService
{
    private readonly AgentTestExecutionLog _log;

    public AgentTestUsageBillingService(AgentTestExecutionLog log)
    {
        _log = log;
    }

    public Task<UsageGateResult> CanProcessAsync(Guid businessId, CancellationToken ct = default)
    {
        _log.Add("usage_gate_checked", "usage_billing", new { businessId, allowed = true });
        return Task.FromResult(new UsageGateResult(true, "test_mode", "Allowed in agent test mode.", null));
    }

    public Task<UsageChargeResult> ChargeAsync(UsageChargeRequest request, CancellationToken ct = default)
    {
        _log.Add("usage_charge_skipped", "usage_billing", new
        {
            request.BusinessId,
            request.AgentId,
            request.ConversationId,
            request.InputTokens,
            request.OutputTokens,
            request.OperationCalls,
            request.OutboundMessages,
            request.Model
        });

        return Task.FromResult(new UsageChargeResult(true, 0, 0, null));
    }

    public Task<BusinessUsageSnapshot?> GetCurrentUsageAsync(Guid businessId, CancellationToken ct = default) =>
        Task.FromResult<BusinessUsageSnapshot?>(null);

    public Task<IReadOnlyList<UsagePlanDto>> GetPlansAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UsagePlanDto>>([]);
}
