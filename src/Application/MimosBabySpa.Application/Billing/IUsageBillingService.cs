namespace MimosBabySpa.Application.Billing;

public interface IUsageBillingService
{
    Task<UsageGateResult> CanProcessAsync(Guid businessId, CancellationToken ct = default);
    Task<UsageChargeResult> ChargeAsync(UsageChargeRequest request, CancellationToken ct = default);
    Task<BusinessUsageSnapshot?> GetCurrentUsageAsync(Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<UsagePlanDto>> GetPlansAsync(CancellationToken ct = default);
}
