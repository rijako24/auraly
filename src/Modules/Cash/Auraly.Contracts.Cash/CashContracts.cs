namespace Auraly.Contracts.Cash;

public static class CashSessionStatuses
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

public sealed record OpenCashSessionRequest(
    Guid BusinessId,
    Guid LocationId,
    decimal OpeningFloat,
    string IdempotencyKey);

public sealed record CashCountLineInput(
    string PaymentMethodCode,
    decimal CountedAmount);

public sealed record HandoffCashRequest(
    Guid ReceivedByUserId,
    IReadOnlyList<CashCountLineInput> Counts,
    string? Observation,
    string? DifferenceReason,
    string SupervisorAuthorizationToken,
    string IdempotencyKey);

public sealed record CloseCashSessionRequest(
    IReadOnlyList<CashCountLineInput> Counts,
    string? Observation,
    string? DifferenceReason,
    string IdempotencyKey);

public sealed record SupervisorAuthorizationRequest(
    string? Username,
    string Credential);

public sealed record SupervisorAuthorizationGrant(
    string Token,
    Guid AuthorizedByUserId,
    string AuthorizedByUserName,
    string PermissionCode,
    DateTimeOffset ExpiresAt);

public sealed record ProvisionSupervisorCredentialRequest(Guid UserId);

public sealed record ProvisionSupervisorCredentialResult(
    Guid CredentialId,
    Guid UserId,
    string UserName,
    string PrintableCredential,
    DateTimeOffset CreatedAt);

public sealed record CashSessionView(
    Guid CashSessionId,
    Guid CashierShiftId,
    Guid BusinessId,
    Guid LocationId,
    Guid RegisterId,
    Guid ResponsibleUserId,
    string ResponsibleUserName,
    DateTimeOffset OpenedAt,
    DateTimeOffset ShiftStartedAt,
    decimal OpeningFloat,
    string Status);

public sealed record CashReconciliationLine(
    string PaymentMethodCode,
    decimal ExpectedAmount,
    decimal CountedAmount,
    decimal DifferenceAmount);

public sealed record CashierReceiptSummary(
    Guid UserId,
    string UserName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DocumentCount,
    decimal NetSales);

public sealed record CashTaxReceiptSummary(
    string TaxCode,
    decimal TaxRate,
    decimal TaxableAmount,
    decimal TaxAmount);

public sealed record CashDailyReceiptSummary(
    DateOnly BusinessDate,
    int DocumentCount,
    decimal NetSales);

public sealed record CashClosureReceipt(
    Guid CashCountId,
    Guid CashSessionId,
    string CountNumber,
    string BusinessName,
    string LocationName,
    string RegisterCode,
    string RegisterName,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    string OpenedBy,
    string ClosedBy,
    decimal OpeningFloat,
    string? FirstDocumentNumber,
    string? LastDocumentNumber,
    string? FirstFiscalNumber,
    string? LastFiscalNumber,
    int SalesCount,
    decimal GrossSales,
    decimal Discounts,
    decimal Returns,
    decimal NetSales,
    decimal CashIn,
    decimal CashOut,
    IReadOnlyList<CashierReceiptSummary> Cashiers,
    IReadOnlyList<CashReconciliationLine> Reconciliation,
    IReadOnlyList<CashTaxReceiptSummary> Taxes,
    IReadOnlyList<CashDailyReceiptSummary> Days,
    string? Observation);

public sealed record CashDailyPaymentSummary(
    string PaymentMethodCode,
    decimal Amount);

public sealed record CashHandoffResult(
    Guid CashCountId,
    CashSessionView Session,
    IReadOnlyList<CashReconciliationLine> Reconciliation);

public sealed record CashDailySummary(
    Guid RegisterId,
    DateOnly BusinessDate,
    int SessionCount,
    int DocumentCount,
    decimal NetSales,
    IReadOnlyList<CashDailyPaymentSummary> Payments);
