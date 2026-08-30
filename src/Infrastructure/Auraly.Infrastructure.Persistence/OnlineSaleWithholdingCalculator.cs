using Auraly.Application.Sales;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Infrastructure.Persistence;

public sealed class OnlineSaleWithholdingCalculator(
    WithholdingService withholdings) : IOnlineSaleWithholdingCalculator
{
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

        return withholdings.CalculateAsync(
            tenantId,
            context.BusinessId,
            new WithholdingPreviewRequest(
                context.BusinessId,
                WithholdingDirections.Sale,
                WithholdingRecognitionMoments.Accrual,
                context.CustomerId.Value,
                null,
                null,
                context.TaxExclusiveAmount,
                context.VatAmount,
                context.OccurredAt),
            cancellationToken);
    }
}
