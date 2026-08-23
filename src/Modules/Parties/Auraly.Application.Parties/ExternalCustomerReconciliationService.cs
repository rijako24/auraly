using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;

namespace Auraly.Application.Parties;

public sealed record ExternalCustomerReconciliationExecution(
    Guid TenantId,
    Guid BusinessId,
    Guid? ActorId,
    string Origin);

public interface IExternalCustomerReconciliationStore
{
    Task<ExternalCustomerReconciliationPage> PageAsync(
        PartyActorIdentity actor,
        int page,
        ExternalCustomerReconciliationQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Guid>> PendingIdsAsync(
        PartyActorIdentity actor,
        int maximumItems,
        CancellationToken cancellationToken);

    Task<ExternalCustomerReconciliationResult> ReconcileAsync(
        ExternalCustomerReconciliationExecution execution,
        Guid externalCommerceCustomerId,
        Guid newPartyId,
        Guid newCustomerId,
        Guid newContactId,
        Guid notificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

}

public sealed class ExternalCustomerReconciliationService(
    IExternalCustomerReconciliationStore store,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<ExternalCustomerReconciliationPage> PageAsync(
        PartyActorIdentity actor,
        int page,
        ExternalCustomerReconciliationQuery query,
        CancellationToken cancellationToken)
    {
        Require(actor, ExternalCustomerReconciliationPermissionCodes.Read);
        if (page < 1 || query.PageSize is < 1 or > 100)
            throw new PartyValidationException("Page and PageSize are outside the allowed range.");
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim();
        if (status is not null && status is not (
                ExternalCustomerReconciliationStatuses.Pending or
                ExternalCustomerReconciliationStatuses.Linked or
                ExternalCustomerReconciliationStatuses.Conflict))
            throw new PartyValidationException("Reconciliation status is invalid.");
        return store.PageAsync(
            actor,
            page,
            query with { Search = query.Search?.Trim(), Status = status },
            cancellationToken);
    }

    public async Task<ExternalCustomerReconciliationResult> ReconcileAsync(
        PartyActorIdentity actor,
        Guid externalCommerceCustomerId,
        CancellationToken cancellationToken)
    {
        Require(actor, ExternalCustomerReconciliationPermissionCodes.Reconcile);
        if (externalCommerceCustomerId == Guid.Empty)
            throw new PartyValidationException("ExternalCommerceCustomerId is required.");
        var result = await store.ReconcileAsync(
            new ExternalCustomerReconciliationExecution(
                actor.TenantId,
                actor.BusinessId,
                actor.ActorId,
                "Manual"),
            externalCommerceCustomerId,
            ids.NewId(),
            ids.NewId(),
            ids.NewId(),
            ids.NewId(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.Status == ExternalCustomerReconciliationStatuses.Linked && !result.IdempotentReplay)
            await synchronization.DispatchPendingAsync(
                actor.TenantId,
                actor.BusinessId,
                CancellationToken.None);
        return result;
    }

    public async Task<ReconcilePendingExternalCustomersResult> ReconcilePendingAsync(
        PartyActorIdentity actor,
        ReconcilePendingExternalCustomersRequest request,
        CancellationToken cancellationToken)
    {
        Require(actor, ExternalCustomerReconciliationPermissionCodes.Reconcile);
        if (request.MaximumItems is < 1 or > 100)
            throw new PartyValidationException("MaximumItems must be between 1 and 100.");
        var pending = await store.PendingIdsAsync(actor, request.MaximumItems, cancellationToken);
        var linked = 0;
        var conflicts = 0;
        var replayed = 0;
        foreach (var id in pending)
        {
            var result = await ReconcileAsync(actor, id, cancellationToken);
            if (result.IdempotentReplay) replayed++;
            else if (result.Status == ExternalCustomerReconciliationStatuses.Linked) linked++;
            else if (result.Status == ExternalCustomerReconciliationStatuses.Conflict) conflicts++;
        }
        return new ReconcilePendingExternalCustomersResult(
            pending.Count,
            linked,
            conflicts,
            replayed);
    }

    private static void Require(PartyActorIdentity actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new PartyForbiddenException($"Permission '{permission}' is required.");
    }
}
