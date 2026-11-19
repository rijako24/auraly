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
            throw new PartyValidationException("La página o la cantidad de registros supera el rango permitido.");
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim();
        if (status is not null && status is not (
                ExternalCustomerReconciliationStatuses.Pending or
                ExternalCustomerReconciliationStatuses.Linked or
                ExternalCustomerReconciliationStatuses.Conflict))
            throw new PartyValidationException("El estado de conciliación no es válido.");
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
            throw new PartyValidationException("Se requiere la identificación del cliente en el sistema externo.");
        var result = await ReconcileCoreAsync(actor, externalCommerceCustomerId, cancellationToken);
        if (result.Status == ExternalCustomerReconciliationStatuses.Linked && !result.IdempotentReplay)
            await synchronization.DispatchTenantPendingAsync(actor.TenantId, CancellationToken.None);
        return result;
    }

    private Task<ExternalCustomerReconciliationResult> ReconcileCoreAsync(
        PartyActorIdentity actor, Guid externalCommerceCustomerId, CancellationToken cancellationToken)
    {
        return store.ReconcileAsync(
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
    }

    public async Task<ReconcilePendingExternalCustomersResult> ReconcilePendingAsync(
        PartyActorIdentity actor,
        ReconcilePendingExternalCustomersRequest request,
        CancellationToken cancellationToken)
    {
        Require(actor, ExternalCustomerReconciliationPermissionCodes.Reconcile);
        if (request.MaximumItems is < 1 or > 100)
            throw new PartyValidationException("La cantidad máxima debe estar entre 1 y 100 registros.");
        var pending = await store.PendingIdsAsync(actor, request.MaximumItems, cancellationToken);
        var linked = 0;
        var conflicts = 0;
        var replayed = 0;
        foreach (var id in pending)
        {
            var result = await ReconcileCoreAsync(actor, id, cancellationToken);
            if (result.IdempotentReplay) replayed++;
            else if (result.Status == ExternalCustomerReconciliationStatuses.Linked) linked++;
            else if (result.Status == ExternalCustomerReconciliationStatuses.Conflict) conflicts++;
        }
        if (linked > 0)
            await synchronization.DispatchTenantPendingAsync(actor.TenantId, CancellationToken.None);
        return new ReconcilePendingExternalCustomersResult(
            pending.Count,
            linked,
            conflicts,
            replayed);
    }

    private static void Require(PartyActorIdentity actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new PartyForbiddenException($"No tienes permiso para realizar esta acción.");
    }
}
