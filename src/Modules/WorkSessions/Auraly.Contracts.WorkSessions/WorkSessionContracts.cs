using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.WorkSessions;

public static class WorkSessionPermissionCodes
{
    public const string Read = "work-sessions.read";
    public const string Open = "work-sessions.open";
    public const string Close = "work-sessions.close";
    public const string ManageCash = "work-sessions.cash.manage";
    public const string ConfigureCashReasons = "work-sessions.cash-reasons.configure";
}

public sealed record WorkSessionIdentity(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public sealed record OpenWorkSessionRequest(
    Guid BusinessId,
    Guid WarehouseId,
    Guid? DeviceId,
    decimal OpeningCash = 0);

public sealed record CloseWorkSessionRequest(
    decimal? CountedCash,
    string? Note,
    Guid? ClosedByUserId = null);

public sealed record DeviceCloseWorkSessionRequest(
    Guid UserId,
    Guid WorkSessionId,
    decimal? CountedCash,
    string? Note,
    Guid AuthorizedByUserId);

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

public sealed record WorkSessionClosurePreviewView(
    Guid WorkSessionId,
    Guid BusinessId,
    string BusinessName,
    Guid WarehouseId,
    string WarehouseName,
    Guid UserId,
    string UserName,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastActivityAt,
    decimal TotalSales,
    decimal TotalRefunds,
    decimal TotalOther,
    decimal NetAmount,
    decimal ExpectedCash,
    IReadOnlyList<WorkSessionPaymentTotal> PaymentTotals);
public static class CashMovementDirections
{
    public const string In = "In";
    public const string Out = "Out";

    public static bool IsSupported(string value) => value is In or Out;
}

public static class CashMovementDocumentTypes
{
    public const string Receipt = AuralyDocumentTypes.CashReceipt;
    public const string Disbursement = AuralyDocumentTypes.CashDisbursement;

    public static string FromDirection(string direction) =>
        direction == CashMovementDirections.In ? Receipt : Disbursement;
}

public sealed record CashMovementReasonView(
    Guid ReasonId,
    Guid BusinessId,
    string Code,
    string Name,
    string Direction,
    string? CounterpartAccountingCategory,
    Guid? DefaultCostCenterId,
    string? DefaultCostCenterName,
    string? AccountCode,
    string? AccountName,
    bool IsAccountingConfigured,
    bool RequiresReference,
    bool IsActive);

public sealed record UpsertCashMovementReasonRequest(
    Guid ReasonId,
    Guid BusinessId,
    string Code,
    string Name,
    string Direction,
    string? CounterpartAccountingCategory,
    Guid? DefaultCostCenterId,
    bool RequiresReference,
    bool IsActive);

public sealed record ConfirmCashMovementRequest(
    Guid DocumentId,
    Guid BusinessId,
    Guid WorkSessionId,
    Guid ReasonId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Reference,
    string? Notes,
    Guid? CostCenterId);

public sealed record CashMovementDocumentPayload(
    Guid TenantId,
    Guid BusinessId,
    Guid DocumentId,
    Guid WorkSessionId,
    Guid ReasonId,
    string ReasonCode,
    string ReasonName,
    string Direction,
    string? CounterpartAccountingCategory,
    Guid? CostCenterId,
    Guid ConfirmedByUserId,
    string DocumentNumber,
    Guid DocumentSeriesId,
    string DocumentPrefix,
    string DocumentSeriesCode,
    long DocumentConsecutive,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Reference,
    string? Notes);
public sealed record DeviceCashMovementRequest(
    Guid UserId,
    ConfirmCashMovementRequest Movement);


public sealed record CashMovementAcceptance(
    Guid DocumentId,
    Guid MovementId,
    string DocumentType,
    string DocumentNumber,
    string Status,
    long ProcessingSequence,
    bool IdempotentReplay);

public static class CashMovementContractSerializer
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string Serialize(CashMovementDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static CashMovementDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<CashMovementDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException(
            "The cash movement payload is invalid.");
}
