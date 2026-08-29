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
        CancellationToken cancellationToken) =>
        withholdings.CalculateAsync(
            tenantId,
            context.BusinessId,
            new WithholdingPreviewRequest(
                context.BusinessId,
                WithholdingDirections.Sale,
                WithholdingRecognitionMoments.Accrual,
                context.CustomerId ?? Guid.Empty,
                null,
                null,
                context.TaxExclusiveAmount,
                context.VatAmount,
                context.OccurredAt),
            cancellationToken);
}
