namespace Auraly.Contracts.Authorization;

public static class PosApprovalStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Reserved = "Reserved";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
    public const string Consumed = "Consumed";
}

public sealed record CreatePosApprovalRequest(
    Guid BusinessId,
    Guid? DeviceId,
    Guid? WorkSessionId,
    Guid DraftId,
    Guid? LineId,
    string PermissionResource,
    string ContextJson);

public sealed record DecidePosApprovalRequest(bool Approve);

public sealed record AuthorizePosApprovalLocallyRequest(string Secret);

public sealed record ConfigureSupervisorCredentialRequest(string Secret, int? ValidityHours = null);

public sealed record SupervisorCredentialStatusView(
    bool IsConfigured,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ValidUntil);


public sealed record ReservePosApprovalForDeviceRequest(
    Guid BusinessId,
    Guid UserId,
    Guid WorkSessionId,
    Guid DraftId,
    Guid? LineId,
    string PermissionResource,
    Guid OperationId);

public sealed record CompletePosApprovalForDeviceRequest(
    Guid BusinessId,
    Guid UserId,
    Guid OperationId);

public sealed record PosApprovalDeviceReservation(
    Guid ApprovalRequestId,
    Guid AuthorizedByUserId,
    Guid OperationId);

public sealed record PosApprovalRequestView(
    Guid ApprovalRequestId,
    Guid TenantId,
    Guid BusinessId,
    Guid? DeviceId,
    Guid? WorkSessionId,
    Guid DraftId,
    Guid? LineId,
    string PermissionResource,
    Guid RequestedByUserId,
    string RequestedByName,
    string ContextJson,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    Guid? DecidedByUserId,
    string? DecidedByName,
    string? DecisionMethod,
    DateTimeOffset? DecidedAt);

public sealed record PosApprovalDecisionResult(
    Guid ApprovalRequestId,
    string Status,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt);
