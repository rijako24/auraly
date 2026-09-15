using Auraly.Application.Sales;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Infrastructure.Persistence;

public sealed class OnlineSaleWithholdingCalculator(
    WithholdingService withholdings) : IOnlineSaleWithholdingCalculator
{
    private readonly Dictionary<(Guid TenantId, Guid BusinessId), WithholdingCalculationPlan>
        _plans = [];

    public async Task WarmAsync(
        Guid tenantId,
        Guid businessId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0 || _plans.ContainsKey((tenantId, businessId)))
            return;
        _plans.Add(
            (tenantId, businessId),
            await withholdings.PrepareCalculationPlanAsync(
                tenantId, businessId, customerIds, cancellationToken));
    }

    public Task<WithholdingCalculationSnapshot> CalculateAsync(
        Guid tenantId,
        OnlineSaleSettlementContext context,
        CancellationToken cancellationToken)
    {
        var gross = decimal.Round(
            context.TaxExclusiveAmount + context.VatAmount,
            4,
            MidpointRounding.AwayFromZero);
        if (context.CustomerId is null)
            return Task.FromResult(new WithholdingCalculationSnapshot(
                gross, 0m, gross, []));

        var request = new WithholdingPreviewRequest(
                context.BusinessId,
                WithholdingDirections.Sale,
                WithholdingRecognitionMoments.Accrual,
                context.CustomerId.Value,
                null,
                null,
                context.TaxExclusiveAmount,
                context.VatAmount,
                context.OccurredAt);
        return _plans.TryGetValue((tenantId, context.BusinessId), out var plan)
            ? Task.FromResult(withholdings.Calculate(plan, request))
            : withholdings.CalculateAsync(
                tenantId, context.BusinessId, request, cancellationToken);
    }
}
