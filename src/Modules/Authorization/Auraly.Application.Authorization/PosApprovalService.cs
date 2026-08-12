using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Authorization;

namespace Auraly.Application.Authorization;

public sealed record PosApprovalUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record SupervisorCredentialVerifier(
    Guid UserId,
    byte[] Salt,
    byte[] Hash,
    int Iterations);

public interface IPosApprovalStore
{
    Task<PosApprovalRequestView> CreateAsync(
        PosApprovalUserIdentity user,
        CreatePosApprovalRequest request,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<PosApprovalRequestView?> GetAsync(
        PosApprovalUserIdentity user,
        Guid approvalRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PosApprovalRequestView>> PendingAsync(
        PosApprovalUserIdentity user,
        Guid businessId,
        CancellationToken cancellationToken);

    Task<PosApprovalDecisionResult> DecideAsync(
        PosApprovalUserIdentity user,
        Guid approvalRequestId,
        bool approve,
        string method,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SupervisorCredentialVerifier>> AuthorizersAsync(
        Guid tenantId,
        Guid businessId,
        string permissionResource,
        CancellationToken cancellationToken);

    Task ConfigureCredentialAsync(
        PosApprovalUserIdentity user,
        byte[] salt,
        byte[] hash,
        int iterations,
        CancellationToken cancellationToken);

    Task ReserveAsync(
        PosApprovalUserIdentity user,
        Guid approvalRequestId,
        Guid businessId,
        Guid draftId,
        Guid? lineId,
        string permissionResource,
        Guid operationId,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        PosApprovalUserIdentity user,
        Guid approvalRequestId,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<PosApprovalUserIdentity?> ResolveDeviceUserAsync(
        Guid tenantId,
        Guid deviceId,
        Guid businessId,
        Guid userId,
        Guid workSessionId,
        CancellationToken cancellationToken);

    Task<PosApprovalDeviceReservation> ReserveForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid approvalRequestId,
        ReservePosApprovalForDeviceRequest request,
        CancellationToken cancellationToken);

    Task CompleteForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid approvalRequestId,
        CompletePosApprovalForDeviceRequest request,
        CancellationToken cancellationToken);
}

public sealed class PosApprovalException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class PosApprovalService(
    IPosApprovalStore store,
    IPosSynchronizationOutboxDispatcher synchronization,
    TimeProvider timeProvider)
{
    private const int Iterations = 210_000;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public async Task<PosApprovalRequestView> CreateAsync(
        PosApprovalUserIdentity user,
        CreatePosApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSensitivePermission(request.PermissionResource);
        EnsureBusiness(user, request.BusinessId);
        if (request.BusinessId == Guid.Empty || request.DraftId == Guid.Empty)
            throw new PosApprovalException("InvalidScope", "La solicitud no identifica el negocio y la venta.");
        if (user.Permissions.Contains(request.PermissionResource))
            throw new PosApprovalException("PermissionAlreadyGranted", "El usuario ya tiene el permiso solicitado.");
        if (string.IsNullOrWhiteSpace(request.ContextJson))
            throw new PosApprovalException("ContextRequired", "La autorización requiere un resumen de la acción.");

        var approval = await store.CreateAsync(
            user, request, timeProvider.GetUtcNow().Add(Lifetime), cancellationToken);
        await synchronization.DispatchPendingAsync(
            user.TenantId, user.BusinessId, CancellationToken.None);
        return approval;
    }

    public async Task<PosApprovalRequestView?> GetAsync(
        PosApprovalUserIdentity user,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default)
    {
        var approval = await store.GetAsync(user, approvalRequestId, cancellationToken);
        if (approval is null) return null;
        EnsureBusiness(user, approval.BusinessId);
        if (approval.RequestedByUserId != user.UserId)
            Require(user, CommercePermissionCodes.PosApprovalsRead);
        return approval;
    }

    public Task<IReadOnlyList<PosApprovalRequestView>> PendingAsync(
        PosApprovalUserIdentity user,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Require(user, CommercePermissionCodes.PosApprovalsRead);
        EnsureBusiness(user, businessId);
        return store.PendingAsync(user, businessId, cancellationToken);
    }

    public async Task<PosApprovalDecisionResult> DecideAsync(
        PosApprovalUserIdentity user,
        Guid approvalRequestId,
        bool approve,
        CancellationToken cancellationToken = default)
    {
        Require(user, CommercePermissionCodes.PosApprovalsAuthorize);
        var approval = await store.GetAsync(user, approvalRequestId, cancellationToken)
            ?? throw new PosApprovalException("NotFound", "La solicitud de autorización no existe.");
        Require(user, approval.PermissionResource);
        EnsureBusiness(user, approval.BusinessId);
        if (approval.RequestedByUserId == user.UserId)
            throw new PosApprovalException("SelfApprovalForbidden", "Quien solicita no puede aprobar su propia acción.");

        var result = await store.DecideAsync(
            user, approvalRequestId, approve, "Remote", cancellationToken);
        await synchronization.DispatchPendingAsync(
            user.TenantId, user.BusinessId, CancellationToken.None);
        return result;
    }

    public async Task<PosApprovalDecisionResult> AuthorizeLocallyAsync(
        PosApprovalUserIdentity requester,
        Guid approvalRequestId,
        string secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new PosApprovalException("CredentialRequired", "Escribe la credencial secundaria del supervisor.");
        var approval = await store.GetAsync(requester, approvalRequestId, cancellationToken)
            ?? throw new PosApprovalException("NotFound", "La solicitud de autorización no existe.");
        EnsureBusiness(requester, approval.BusinessId);

        var candidates = await store.AuthorizersAsync(
            requester.TenantId, approval.BusinessId, approval.PermissionResource, cancellationToken);
        var secretBytes = System.Text.Encoding.UTF8.GetBytes(secret);
        Guid? authorizer = null;
        foreach (var candidate in candidates)
        {
            var derived = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                secretBytes, candidate.Salt, candidate.Iterations,
                System.Security.Cryptography.HashAlgorithmName.SHA256, candidate.Hash.Length);
            if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(derived, candidate.Hash))
                authorizer = candidate.UserId;
        }

        if (authorizer is null)
            throw new PosApprovalException("InvalidCredential", "La credencial no corresponde a un supervisor autorizado.");
        if (authorizer.Value == requester.UserId)
            throw new PosApprovalException("SelfApprovalForbidden", "Quien solicita no puede aprobar su propia acción.");

        var authorizerIdentity = new PosApprovalUserIdentity(
            authorizer.Value,
            requester.TenantId,
            requester.BusinessId,
            new HashSet<string>(StringComparer.Ordinal)
            {
                CommercePermissionCodes.PosApprovalsAuthorize,
                approval.PermissionResource
            });
        var result = await store.DecideAsync(
            authorizerIdentity, approvalRequestId, true, "LocalSecret", cancellationToken);
        await synchronization.DispatchPendingAsync(
            requester.TenantId, requester.BusinessId, CancellationToken.None);
        return result;
    }

    public async Task<T> ExecuteSensitiveAsync<T>(
        PosApprovalUserIdentity user,
        Guid approvalRequestId,
        Guid businessId,
        Guid draftId,
        Guid? lineId,
        string permissionResource,
        Guid operationId,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness(user, businessId);
        ValidateSensitivePermission(permissionResource);
        if (user.Permissions.Contains(permissionResource))
            return await action();
        if (approvalRequestId == Guid.Empty)
            throw new PosApprovalException("ApprovalRequired", "Esta acción requiere aprobación de un supervisor.");

        await store.ReserveAsync(
            user, approvalRequestId, businessId, draftId, lineId,
            permissionResource, operationId, cancellationToken);
        var result = await action();
        await store.CompleteAsync(
            user, approvalRequestId, operationId, cancellationToken);
        return result;
    }

    public async Task<PosApprovalRequestView> CreateForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid userId,
        Guid workSessionId,
        CreatePosApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DeviceId != deviceId || request.WorkSessionId != workSessionId)
            throw new PosApprovalException("InvalidScope", "La solicitud no corresponde al dispositivo y sesión locales.");
        var user = await store.ResolveDeviceUserAsync(
            tenantId, deviceId, request.BusinessId, userId, workSessionId, cancellationToken)
            ?? throw new PosApprovalException("Forbidden", "El usuario no tiene una sesión abierta en este dispositivo.");
        return await CreateAsync(user, request, cancellationToken);
    }

    public Task<PosApprovalDeviceReservation> ReserveForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid approvalRequestId,
        ReservePosApprovalForDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSensitivePermission(request.PermissionResource);
        if (tenantId == Guid.Empty || deviceId == Guid.Empty ||
            approvalRequestId == Guid.Empty || request.BusinessId == Guid.Empty ||
            request.UserId == Guid.Empty || request.WorkSessionId == Guid.Empty ||
            request.DraftId == Guid.Empty || request.OperationId == Guid.Empty)
            throw new PosApprovalException("InvalidScope", "La aprobación remota no identifica completamente el dispositivo y la venta.");
        return store.ReserveForDeviceAsync(
            tenantId, deviceId, approvalRequestId, request, cancellationToken);
    }

    public Task CompleteForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid approvalRequestId,
        CompletePosApprovalForDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || deviceId == Guid.Empty ||
            approvalRequestId == Guid.Empty || request.BusinessId == Guid.Empty ||
            request.UserId == Guid.Empty || request.OperationId == Guid.Empty)
            throw new PosApprovalException("InvalidScope", "La finalización remota no identifica completamente la autorización.");
        return store.CompleteForDeviceAsync(
            tenantId, deviceId, approvalRequestId, request, cancellationToken);
    }

    public Task ConfigureCredentialAsync(
        PosApprovalUserIdentity user,
        string secret,
        CancellationToken cancellationToken = default)
    {
        Require(user, CommercePermissionCodes.PosApprovalsManageCredential);
        Require(user, CommercePermissionCodes.PosApprovalsAuthorize);
        if (string.IsNullOrWhiteSpace(secret) || secret.Length is < 6 or > 32)
            throw new PosApprovalException("WeakCredential", "La credencial debe tener entre 6 y 32 caracteres.");

        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            secret, salt, Iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return store.ConfigureCredentialAsync(user, salt, hash, Iterations, cancellationToken);
    }

    private static void Require(PosApprovalUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PosApprovalException("Forbidden", $"Permission '{permission}' is required.");
    }

    private static void EnsureBusiness(PosApprovalUserIdentity user, Guid businessId)
    {
        if (user.BusinessId == Guid.Empty || user.BusinessId != businessId)
            throw new PosApprovalException("Forbidden", "El negocio no coincide con el contexto autenticado.");
    }

    private static void ValidateSensitivePermission(string permission)
    {
        if (permission is not (
            CommercePermissionCodes.SalesDiscount or
            CommercePermissionCodes.SalesRemoveLine or
            CommercePermissionCodes.SalesRestartDraft))
            throw new PosApprovalException("UnsupportedPermission", "La acción no admite autorización delegada.");
    }
}
