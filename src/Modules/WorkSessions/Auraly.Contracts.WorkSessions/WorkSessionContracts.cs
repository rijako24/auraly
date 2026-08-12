namespace Auraly.Contracts.WorkSessions;

public static class WorkSessionPermissionCodes
{
    public const string Read = "work-sessions.read";
    public const string Open = "work-sessions.open";
    public const string Close = "work-sessions.close";
}

public sealed record WorkSessionIdentity(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public sealed record OpenWorkSessionRequest(
    Guid BusinessId,
    Guid WarehouseId,
    Guid? DeviceId);

public sealed record CloseWorkSessionRequest(
    decimal? CountedCash,
    string? Note);

public sealed record WorkSessionView(
    Guid WorkSessionId,
    Guid BusinessId,
    string BusinessName,
    Guid WarehouseId,
    string WarehouseName,
    Guid UserId,
    string UserName,
    Guid? DeviceId,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastActivityAt,
    string Status);

public sealed record WorkSessionPaymentTotal(
    string PaymentMethodCode,
    decimal SalesAmount,
    decimal RefundAmount,
    decimal OtherAmount,
    decimal NetAmount);

public sealed record WorkSessionClosureView(
    Guid WorkSessionClosureId,
    Guid WorkSessionId,
    Guid BusinessId,
    string BusinessName,
    Guid WarehouseId,
    string WarehouseName,
    Guid UserId,
    string UserName,
    Guid? DeviceId,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal TotalSales,
    decimal TotalRefunds,
    decimal TotalOther,
    decimal NetAmount,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? CashDifference,
    string? Note,
    IReadOnlyList<WorkSessionPaymentTotal> PaymentTotals);
