using Auraly.Application.DocumentProcessing;
using Auraly.Domain.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Auraly.Commerce.Accounting.Application;

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

    Task<WorkSessionClosurePreviewView> PreviewClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkSessionCashDifferenceView>> ListCashDifferencesAsync(
        WorkSessionIdentity identity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CashMovementReasonView>> ListCashReasonsAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        string? direction,
        CancellationToken cancellationToken);

    Task<CashMovementReasonView> UpsertCashReasonAsync(
        WorkSessionIdentity identity,
        CashMovementReasonDefinition reason,
        CancellationToken cancellationToken);

    Task<CashMovementReasonDefinition?> FindCashReasonAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        Guid reasonId,
        CancellationToken cancellationToken);

    Task<CashMovementAcceptance> AcceptCashMovementAsync(
        WorkSessionIdentity identity,
        string idempotencyKey,
        CashMovement movement,
        CancellationToken cancellationToken);
}

public sealed class WorkSessionService(
    IWorkSessionStore store,
    IDocumentProcessingSignalPublisher signalPublisher,
    AccountingProcessingCoordinator accountingProcessing)
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
        if (request.OpeningCash < 0)
            throw new WorkSessionValidationException(
                "Opening cash cannot be negative.");
        return store.OpenOrResumeAsync(identity, request, cancellationToken);
    }

    public async Task<WorkSessionClosureView> CloseAsync(
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
        if (request.PaymentCounts is not null &&
            (request.PaymentCounts.Any(value =>
                string.IsNullOrWhiteSpace(value.PaymentMethodCode) ||
                value.PaymentMethodCode.Trim().Length > 32 ||
                value.CountedAmount < 0) ||
             request.PaymentCounts.Select(value => value.PaymentMethodCode.Trim())
                 .Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.PaymentCounts.Count))
            throw new WorkSessionValidationException(
                "Each payment method requires one valid non-negative counted amount.");
        if (request.ClosedByUserId == Guid.Empty)
            throw new WorkSessionValidationException(
                "The supervisor identifier is invalid.");
        if (request.Note?.Trim().Length > 500)
            throw new WorkSessionValidationException(
                "The closure note cannot exceed 500 characters.");
        var closure = await store.CloseAsync(
            identity,
            workSessionId,
            idempotencyKey.Trim(),
            request with { Note = NullIfWhiteSpace(request.Note) },
            cancellationToken);
        if (closure.CashDifference is not null && closure.CashDifference != 0)
            await accountingProcessing.RequestPostingAsync(
                closure.BusinessId,
                closure.WorkSessionClosureId,
                WorkSessionAccountingDocumentTypes.CashDifference,
                cancellationToken);
        return closure;
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

    public Task<WorkSessionClosurePreviewView> PreviewClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Close);
        if (workSessionId == Guid.Empty)
            throw new WorkSessionValidationException("WorkSessionId is required.");
        return store.PreviewClosureAsync(identity, workSessionId, cancellationToken);
    }

    public Task<IReadOnlyList<WorkSessionCashDifferenceView>> ListCashDifferencesAsync(
        WorkSessionIdentity identity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ReadCashDifferences);
        if (from == default || to == default || from > to || to.DayNumber - from.DayNumber > 366)
            throw new WorkSessionValidationException(
                "The cash-difference date range must contain at most 367 days.");
        return store.ListCashDifferencesAsync(identity, from, to, cancellationToken);
    }

    public Task<IReadOnlyList<CashMovementReasonView>> ListCashReasonsAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        string? direction,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.Read);
        if (businessId == Guid.Empty)
            throw new WorkSessionValidationException("BusinessId is required.");
        var normalizedDirection = NullIfWhiteSpace(direction);
        if (normalizedDirection is not null &&
            !CashMovementDirections.IsSupported(normalizedDirection))
            throw new WorkSessionValidationException(
                "Direction must be In or Out.");
        return store.ListCashReasonsAsync(
            identity, businessId, normalizedDirection, cancellationToken);
    }

    public Task<CashMovementReasonView> UpsertCashReasonAsync(
        WorkSessionIdentity identity,
        UpsertCashMovementReasonRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ConfigureCashReasons);
        if (!Enum.TryParse<CashMovementDirection>(
                request.Direction, ignoreCase: false, out var direction))
            throw new WorkSessionValidationException(
                "Direction must be In or Out.");
        try
        {
            var reason = CashMovementReasonDefinition.Create(
                request.ReasonId,
                request.BusinessId,
                request.Code,
                request.Name,
                direction,
                request.CounterpartAccountingCategory,
                request.DefaultCostCenterId,
                request.RequiresReference,
                request.IsActive);
            return store.UpsertCashReasonAsync(
                identity, reason, cancellationToken);
        }
        catch (CashMovementRuleException exception)
        {
            throw new WorkSessionValidationException(exception.Message);
        }
    }

    public async Task<CashMovementAcceptance> ConfirmCashMovementAsync(
        WorkSessionIdentity identity,
        string idempotencyKey,
        ConfirmCashMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(identity, WorkSessionPermissionCodes.ManageCash);
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Trim().Length > 160)
            throw new WorkSessionValidationException(
                "A valid Idempotency-Key header is required.");
        var reason = await store.FindCashReasonAsync(
            identity,
            request.BusinessId,
            request.ReasonId,
            cancellationToken)
            ?? throw new WorkSessionValidationException(
                "The selected cash movement reason does not exist or is inactive.");
        CashMovement movement;
        try
        {
            movement = CashMovement.Create(
                request.DocumentId,
                request.BusinessId,
                request.WorkSessionId,
                reason,
                request.Amount,
                request.OccurredAt,
                request.Reference,
                request.Notes,
                request.CostCenterId);
        }
        catch (CashMovementRuleException exception)
        {
            throw new WorkSessionValidationException(exception.Message);
        }

        var acceptance = await store.AcceptCashMovementAsync(
            identity,
            idempotencyKey.Trim(),
            movement,
            cancellationToken);
        await signalPublisher.PublishAsync(
            new DocumentProcessingSignal(
                acceptance.MovementId,
                request.BusinessId,
                request.DocumentId,
                acceptance.DocumentType),
            cancellationToken);
        return acceptance;
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
