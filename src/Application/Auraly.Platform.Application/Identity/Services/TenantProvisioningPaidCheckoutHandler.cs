using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantProvisioningPaidCheckoutHandler(
    ITenantProvisioningCheckoutStore store,
    ITenantService tenants,
    ILogger<TenantProvisioningPaidCheckoutHandler> logger)
    : INonConversationalPaidCheckoutHandler
{
    public CheckoutKind Kind => CheckoutKind.TenantProvisioning;

    public async Task<bool> IsFulfilledAsync(
        PaymentTransaction payment,
        CancellationToken cancellationToken)
    {
        if (payment.SubjectType != "TenantProvisioning" || payment.SubjectId is not Guid draftId)
            return false;
        return string.Equals((await store.GetForFulfillmentAsync(draftId, cancellationToken))?.Status,
            "Provisioned", StringComparison.Ordinal);
    }

    public async Task FulfillAsync(PaymentTransaction payment, CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentTransactionStatus.Confirmed
            || payment.SubjectType != "TenantProvisioning"
            || payment.SubjectId is not Guid draftId)
            throw new InvalidOperationException("El pago no corresponde a un aprovisionamiento confirmado.");
        var fulfillment = await store.GetForFulfillmentAsync(draftId, cancellationToken)
            ?? throw new InvalidOperationException("No existe el borrador de aprovisionamiento pagado.");
        if (fulfillment.PaymentTransactionId != payment.PaymentTransactionId)
            throw new InvalidOperationException("El pago no coincide con el borrador de aprovisionamiento.");
        if (fulfillment.Status == "Provisioned") return;

        try
        {
            var result = await tenants.ProvisionAsync(fulfillment.Snapshot.Tenant, null,
                fulfillment.Snapshot.Quote, cancellationToken);
            await store.MarkProvisionedAsync(draftId, result.TenantId, cancellationToken);
            logger.LogInformation(
                "Paid tenant provisioning completed DraftId={DraftId} TenantId={TenantId} PaymentTransactionId={PaymentTransactionId}",
                draftId, result.TenantId, payment.PaymentTransactionId);
        }
        catch (Exception exception)
        {
            await store.MarkFailedAsync(draftId, "No fue posible completar el aprovisionamiento. Auraly lo reintentará.", cancellationToken);
            logger.LogError(exception,
                "Paid tenant provisioning failed DraftId={DraftId} PaymentTransactionId={PaymentTransactionId}",
                draftId, payment.PaymentTransactionId);
            throw;
        }
    }
}
