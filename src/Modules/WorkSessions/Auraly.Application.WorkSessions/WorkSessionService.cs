using Auraly.Contracts.WorkSessions;

namespace Auraly.Application.WorkSessions;

public interface IWorkSessionStore
{
    Task<WorkSessionView?> CurrentAsync(
        WorkSessionIdentity identity,
        CancellationToken cancellationToken);

    Task<WorkSessionView> OpenOrResumeAsync(
        WorkSessionIdentity identity,
        OpenWorkSessionRequest request,
        CancellationToken cancellationToken);

    Task<WorkSessionClosureView> CloseAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        string idempotencyKey,
        CloseWorkSessionRequest request,
        CancellationToken cancellationToken);

    Task<WorkSessionClosureView?> CloseForAuthenticationAsync(
        Guid userId,
        Guid authenticationSessionId,
        string reason,
        CancellationToken cancellationToken);

    Task<WorkSessionClosureView?> GetClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken);
}

public sealed class WorkSessionService(IWorkSessionStore store)
{
    public Task<WorkSessionView?> CurrentAsync(
        WorkSessionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Read);
        return store.CurrentAsync(identity, cancellationToken);
    }

    public Task<WorkSessionView> OpenOrResumeAsync(
        WorkSessionIdentity identity,
        OpenWorkSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Open);
        if (request.BusinessId == Guid.Empty || request.WarehouseId == Guid.Empty)
            throw new WorkSessionValidationException(
                "BusinessId and WarehouseId are required.");
        if (request.DeviceId == Guid.Empty)
            throw new WorkSessionValidationException(
                "DeviceId must be null or a valid identifier.");
        return store.OpenOrResumeAsync(identity, request, cancellationToken);
    }

    public Task<WorkSessionClosureView> CloseAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        string idempotencyKey,
        CloseWorkSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Close);
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("WorkSessionId is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new WorkSessionValidationException(
                "A valid Idempotency-Key header is required.");
        if (request.CountedCash < 0)
            throw new WorkSessionValidationException(
                "Counted cash cannot be negative.");
        if (request.Note?.Trim().Length > 500)
            throw new WorkSessionValidationException(
                "The closure note cannot exceed 500 characters.");
        return store.CloseAsync(
            identity,
            workSessionId,
            idempotencyKey.Trim(),
            request with { Note = NullIfWhiteSpace(request.Note) },
            cancellationToken);
    }

    public Task<WorkSessionClosureView?> CloseForLoginAsync(
        Guid userId,
        Guid tenantId,
        Guid authenticationSessionId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || tenantId == Guid.Empty ||
            authenticationSessionId == Guid.Empty)
            throw new WorkSessionValidationException(
                "The login context is incomplete.");
        return store.CloseForAuthenticationAsync(
            userId,
            authenticationSessionId,
            "login-replacement",
            cancellationToken);
    }

    public Task<WorkSessionClosureView?> CloseForLogoutAsync(
        Guid userId,
        Guid tenantId,
        Guid authenticationSessionId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || tenantId == Guid.Empty ||
            authenticationSessionId == Guid.Empty)
            throw new WorkSessionValidationException(
                "The logout context is incomplete.");
        return store.CloseForAuthenticationAsync(
            userId,
            authenticationSessionId,
            "logout",
            cancellationToken);
    }

    public Task<WorkSessionClosureView?> GetClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Read);
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("WorkSessionId is required.");
        return store.GetClosureAsync(identity, workSessionId, cancellationToken);
    }

    private static void Demand(WorkSessionIdentity identity, string permission)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.UserId == Guid.Empty || identity.TenantId == Guid.Empty)
            throw new WorkSessionForbiddenException(
                "The authenticated user context is incomplete.");
        if (!identity.Permissions.Contains(permission))
            throw new WorkSessionForbiddenException(
                $"Permission '{permission}' is required.");
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class WorkSessionForbiddenException(string message) : Exception(message);
public sealed class WorkSessionValidationException(string message) : Exception(message);
public sealed class WorkSessionConflictException(string message) : Exception(message);
public sealed class WorkSessionNotFoundException(string message) : Exception(message);
