using Auraly.Contracts.Authorization;
using Auraly.Contracts.Cash;

namespace Auraly.Application.Cash;

public sealed record CashUserIdentity(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public interface ICashSessionStore
{
    Task<CashSessionView?> CurrentAsync(
        CashUserIdentity actor, Guid registerId, CancellationToken ct);
    Task<CashSessionView> OpenOrResumeAsync(
        CashUserIdentity actor, Guid registerId, OpenCashSessionRequest request, CancellationToken ct);
    Task<CashHandoffResult> HandoffAsync(
        CashUserIdentity actor, Guid registerId, HandoffCashRequest request, CancellationToken ct);
    Task<CashClosureReceipt> CloseAsync(
        CashUserIdentity actor, Guid registerId, CloseCashSessionRequest request, CancellationToken ct);
    Task<CashClosureReceipt?> ReceiptAsync(
        CashUserIdentity actor, Guid cashCountId, CancellationToken ct);
    Task<CashDailySummary> DailyAsync(
        CashUserIdentity actor, Guid registerId, DateOnly businessDate, CancellationToken ct);
    Task<SupervisorAuthorizationGrant> AuthorizeHandoffAsync(
        CashUserIdentity actor, Guid registerId, SupervisorAuthorizationRequest request,
        CancellationToken ct);
    Task<ProvisionSupervisorCredentialResult> ProvisionSupervisorCredentialAsync(
        CashUserIdentity actor, ProvisionSupervisorCredentialRequest request,
        CancellationToken ct);
}

public sealed class CashForbiddenException(string message) : Exception(message);
public sealed class CashConflictException(string message) : Exception(message);
public sealed class CashValidationException(string message) : Exception(message);
public sealed class CashNotFoundException(string message) : Exception(message);

public sealed class CashSessionService(ICashSessionStore store)
{
    public Task<CashSessionView?> CurrentAsync(
        CashUserIdentity actor, Guid registerId, CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.CashRead);
        ValidateRegister(registerId);
        return store.CurrentAsync(actor, registerId, ct);
    }

    public Task<CashSessionView> OpenOrResumeAsync(
        CashUserIdentity actor,
        Guid registerId,
        OpenCashSessionRequest request,
        CancellationToken ct = default)
    {
        DemandAny(actor, CommercePermissionCodes.SalesCreate, CommercePermissionCodes.CashOpen);
        ValidateRegister(registerId);
        if (request.BusinessId == Guid.Empty)
            throw new CashValidationException("La sede es obligatoria.");
        if (request.OpeningFloat < 0)
            throw new CashValidationException("El fondo inicial no puede ser negativo.");
        ValidateKey(request.IdempotencyKey);
        return store.OpenOrResumeAsync(actor, registerId, request, ct);
    }

    public Task<CashHandoffResult> HandoffAsync(
        CashUserIdentity actor,
        Guid registerId,
        HandoffCashRequest request,
        CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.SalesCreate);
        ValidateRegister(registerId);
        if (request.ReceivedByUserId == Guid.Empty || request.ReceivedByUserId == actor.UserId)
            throw new CashValidationException("Selecciona otro usuario para recibir la caja.");
        ValidateCounts(request.Counts);
        if (string.IsNullOrWhiteSpace(request.SupervisorAuthorizationToken))
            throw new CashValidationException("La autorización del supervisor es obligatoria.");
        ValidateKey(request.IdempotencyKey);
        return store.HandoffAsync(actor, registerId, request, ct);
    }

    public Task<CashClosureReceipt> CloseAsync(
        CashUserIdentity actor,
        Guid registerId,
        CloseCashSessionRequest request,
        CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.CashClose);
        ValidateRegister(registerId);
        ValidateCounts(request.Counts);
        ValidateKey(request.IdempotencyKey);
        return store.CloseAsync(actor, registerId, request, ct);
    }

    public Task<CashClosureReceipt?> ReceiptAsync(
        CashUserIdentity actor, Guid cashCountId, CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.CashRead);
        if (cashCountId == Guid.Empty)
            throw new CashValidationException("El arqueo es obligatorio.");
        return store.ReceiptAsync(actor, cashCountId, ct);
    }

    public Task<CashDailySummary> DailyAsync(
        CashUserIdentity actor, Guid registerId, DateOnly date, CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.CashRead);
        ValidateRegister(registerId);
        return store.DailyAsync(actor, registerId, date, ct);
    }

    public Task<SupervisorAuthorizationGrant> AuthorizeHandoffAsync(
        CashUserIdentity actor,
        Guid registerId,
        SupervisorAuthorizationRequest request,
        CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.SalesCreate);
        ValidateRegister(registerId);
        if (string.IsNullOrWhiteSpace(request.Credential) || request.Credential.Length > 512)
            throw new CashValidationException("Ingresa una credencial de supervisor válida.");
        return store.AuthorizeHandoffAsync(actor, registerId, request, ct);
    }

    public Task<ProvisionSupervisorCredentialResult> ProvisionSupervisorCredentialAsync(
        CashUserIdentity actor,
        ProvisionSupervisorCredentialRequest request,
        CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.SupervisorCredentialsManage);
        if (request.UserId == Guid.Empty)
            throw new CashValidationException("El supervisor es obligatorio.");
        return store.ProvisionSupervisorCredentialAsync(actor, request, ct);
    }

    private static void Demand(CashUserIdentity actor, string permission)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId == Guid.Empty || actor.TenantId == Guid.Empty ||
            !actor.Permissions.Contains(permission))
            throw new CashForbiddenException($"Permission '{permission}' is required.");
    }

    private static void DemandAny(CashUserIdentity actor, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId == Guid.Empty || actor.TenantId == Guid.Empty ||
            !permissions.Any(actor.Permissions.Contains))
            throw new CashForbiddenException(
                $"One of these permissions is required: {string.Join(", ", permissions)}.");
    }

    private static void ValidateRegister(Guid registerId)
    {
        if (registerId == Guid.Empty)
            throw new CashValidationException("La caja es obligatoria.");
    }

    private static void ValidateCounts(IReadOnlyList<CashCountLineInput> counts)
    {
        if (counts is null || counts.Count == 0)
            throw new CashValidationException("Registra al menos un medio de pago contado.");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new CashValidationException(
                "La clave de idempotencia es obligatoria y admite hasta 128 caracteres.");
    }
}
