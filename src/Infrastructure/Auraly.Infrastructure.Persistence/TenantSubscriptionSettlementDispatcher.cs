using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Sales;
using Auraly.Platform.Application.Identity.Services;

namespace Auraly.Infrastructure.Persistence;

public sealed class TenantSubscriptionSettlementDispatcher(
    AccountingProcessingCoordinator accounting,
    FiscalProcessingCoordinator fiscal,
    SalesReportingProcessingCoordinator reporting)
    : ITenantSubscriptionSettlementDispatcher
{
    public async Task DispatchAsync(
        TenantSubscriptionSettlementResult settlement,
        CancellationToken cancellationToken)
    {
        await accounting.RequestPostingAsync(
            settlement.BusinessId, settlement.DocumentId,
            ServiceInvoiceDocumentTypes.ServiceInvoice, cancellationToken);
        await reporting.RequestProjectionAsync(
            settlement.BusinessId, settlement.DocumentId,
            ServiceInvoiceDocumentTypes.ServiceInvoice, cancellationToken);
        await fiscal.RequestGenerationAsync(
            settlement.BusinessId, settlement.DocumentId, cancellationToken);
    }
}
