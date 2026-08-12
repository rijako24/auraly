using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;

namespace Auraly.Application.Parties;

public sealed record ExternalCustomerReconciliationExecution(
    Guid TenantId,
    Guid BusinessId,
    Guid? ActorId,
    string Origin);

public sealed record ExternalCustomerReconciliationReceipt(
    Guid ExternalCommerceCustomerId,
    Guid BusinessId,
    string Status);

public sealed record ExternalCustomerReconciliationSignalResult(
    Guid MessageId,
    Guid ExternalCommerceCustomerId,
    string Status,
    bool IdempotentReplay);

public sealed class ExternalCustomerReconciliationSystemService(
    IExternalCustomerReconciliationStore store,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public async Task<ExternalCustomerReconciliationSignalResult> ProcessAsync(
        ExternalCustomerReconciliationSignal signal,
        CancellationToken cancellationToken)
    {
        ExternalCustomerReconciliationSignalCodec.Validate(signal);
        var receipt = await store.ReceiptStatusAsync(signal.MessageId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.ExternalCommerceCustomerId != signal.ExternalCommerceCustomerId ||
                receipt.BusinessId != signal.BusinessId)
                throw new InvalidOperationException(
                    "The reconciliation message ID was already used for another external customer.");
            return new ExternalCustomerReconciliationSignalResult(
                signal.MessageId,
                signal.ExternalCommerceCustomerId,
                receipt.Status,
                true);
        }

        var execution = await store.ResolveIntegrationExecutionAsync(
            signal.BusinessId,
            signal.ExternalCommerceCustomerId,
            cancellationToken);
        var result = await store.ReconcileAsync(
            execution,
            signal.ExternalCommerceCustomerId,
            ids.NewId(),
            ids.NewId(),
            ids.NewId(),
            ids.NewId(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        await store.RecordReceiptAsync(
            signal.MessageId,
            signal.ExternalCommerceCustomerId,
            signal.BusinessId,
            result.Status,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.Status == ExternalCustomerReconciliationStatuses.Linked &&
            !result.IdempotentReplay)
            await synchronization.DispatchPendingAsync(
                execution.TenantId,
                execution.BusinessId,
                CancellationToken.None);
        return new ExternalCustomerReconciliationSignalResult(
            signal.MessageId,
            signal.ExternalCommerceCustomerId,
            result.Status,
            result.IdempotentReplay);
    }
}
