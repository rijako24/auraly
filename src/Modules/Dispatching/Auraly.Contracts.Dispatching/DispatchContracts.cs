using System.Text.Json.Serialization;

namespace Auraly.Contracts.Dispatching;

public static class DispatchLimits
{
    public const int MaximumDocuments=500;
    public const int MaximumProductLines=10_000;
    public const int MaximumPaymentsPerDocument=20;
    public const int MaximumCollectedPayments=MaximumDocuments*MaximumPaymentsPerDocument;
}

public static class DispatchAccountingDocumentTypes
{
    public const string CashDifference = "DispatchCashDifference";
}

public sealed record DispatchCashDifferencePayload(
    Guid DispatchSettlementId,
    Guid TenantId,
    Guid BusinessId,
    Guid DispatchId,
    string DispatchNumber,
    string TransporterName,
    decimal ExpectedCash,
    decimal CashReceived,
    decimal Difference,
    DateTimeOffset OccurredAt,
    string? Notes,
    IReadOnlyList<DispatchCollectedPayment>? Collections = null, Guid? CashDifferencePaymentMethodId = null);

public sealed record DispatchCollectedPayment(Guid PaymentId, Guid SourceDocumentId,
    Guid PaymentMethodId, string MethodCode, decimal Amount);

public static class DispatchPermissionCodes
{
    public const string Read = "dispatches.read";
    public const string ReadAll = "dispatches.read-all";
    public const string ExecuteDeliveries = "dispatches.delivery.execute";
    public const string Settle = "dispatches.settle";
    public const string Create = "dispatches.create";
    public const string EditDraft = "dispatches.edit-draft";
    public const string AttachDocuments = "dispatches.attach-documents";
    public const string Prepare = "dispatches.prepare";
    public const string Verify = "dispatches.verify";
    public const string CorrectVerification = "dispatches.correct-verification";
    public const string DeclareShortage = "dispatches.declare-shortage";
    public const string Release = "dispatches.release";
    public const string Cancel = "dispatches.cancel";
    public const string Reopen = "dispatches.reopen";
    public const string Reports = "dispatches.reports.view";
    public const string Export = "dispatches.reports.export";
    public const string ViewPrices = "dispatches.view-prices";
}

public static class DispatchStatuses
{
    public const string Draft = "Draft";
    public const string Prepared = "Prepared";
    public const string InVerification = "InVerification";
    public const string Verified = "Verified";
    public const string Released = "Released";
    public const string InDelivery = "InDelivery";
    public const string PendingSettlement = "PendingSettlement";
    public const string SettlementProcessing = "SettlementProcessing";
    public const string SettlementAttention = "SettlementAttention";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

public static class DeliveryStatuses
{
    public const string Delivered = "Delivered";
    public const string PartiallyDelivered = "PartiallyDelivered";
    public const string NotDelivered = "NotDelivered";
}

public static class DeliveryPaymentApplications
{
    public const string InvoicePayment = "InvoicePayment";
    public const string CreditDocument = "CreditDocument";
    public const string CreditAdvance = "CreditAdvance";
    public const string KeepCurrent = "KeepCurrent";
}

public sealed record DispatchActorIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions,
    bool IsAdministrator);
public sealed record DispatchQuery(int Page = 1, int PageSize = 25, string? Search = null, string? Status = null, DateOnly? From = null, DateOnly? To = null, bool Mine = false, string OrderBy = "scheduled-desc");
public sealed record DispatchListItem(Guid DispatchId, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, string Status, int DocumentCount, int LineCount, decimal ExpectedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, DateTimeOffset UpdatedAt, string RowVersion);
public sealed record DispatchPage(IReadOnlyCollection<DispatchListItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record DispatchCandidateQuery(int Page = 1, int PageSize = 50, string? Search = null, string? DocumentType = null, DateOnly? From = null, DateOnly? To = null, Guid? WarehouseId = null, int DateRangeVersion = 1, Guid? RouteId = null);
public sealed record DispatchCandidateDocument(Guid DocumentId, string DocumentType, string DocumentNumber, DateTimeOffset IssuedAt, Guid WarehouseId, string WarehouseName, Guid? CustomerId, string CustomerName, string? DeliveryAddress, string SellerName, int LineCount, decimal PendingQuantity, decimal DocumentTotal)
{
    public string? FiscalNumber { get; init; }
}
public sealed record DispatchCandidatePage(IReadOnlyCollection<DispatchCandidateDocument> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record CreateDispatchRequest(Guid BusinessId, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, Guid? DriverCarrierId, string? Notes, IReadOnlyCollection<Guid> SourceDocumentIds);
public sealed record AddDispatchDocumentsRequest(IReadOnlyCollection<Guid> SourceDocumentIds, string RowVersion);
public sealed record DispatchTransitionRequest(string RowVersion, string IdempotencyKey);
public sealed record DispatchVerificationRequest(Guid DispatchLineId, decimal QuantityDelta, string? Barcode, string IdempotencyKey, DateTimeOffset OccurredAt);
public sealed record DeclareDispatchShortageRequest(Guid DispatchLineId, decimal Quantity, string Reason, string? Notes, string RowVersion, string IdempotencyKey);

public sealed record DispatchMutationResult(Guid DispatchId, string DispatchNumber, string Status, int DocumentCount, int LineCount, decimal ExpectedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, string RowVersion, DispatchLineDetail? UpdatedLine = null, int PendingLineCount = 0);
public sealed record DispatchDetailQuery(int DocumentPage = 1, int DocumentPageSize = 10, int LinePage = 1, int LinePageSize = 10);
public sealed record DispatchDocumentDetail(Guid DispatchSourceDocumentId, Guid SourceDocumentId, string DocumentType, string DocumentNumber, Guid? CustomerId, string CustomerName, string? DeliveryAddress, string SellerName, decimal DocumentTotal, string Status, Guid WarehouseId, string WarehouseName)
{
    public string? FiscalNumber { get; init; }
}
public sealed record DispatchLineDetail(Guid DispatchLineId, Guid DispatchSourceDocumentId, int SourceLineNumber, Guid ProductId, string ProductCode, string Description, decimal AssignedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, string Status, string RowVersion);
public sealed record DispatchShortageDetail(Guid DispatchShortageId, Guid DispatchLineId, Guid ProductId, string ProductCode, string Description, decimal Quantity, string Reason, string? Notes, DateTimeOffset CreatedAt);
public sealed record DispatchDetail(Guid DispatchId, Guid BusinessId, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, Guid? RouteId, string? RouteName, string? Notes, string Status, IReadOnlyCollection<DispatchDocumentDetail> Documents, IReadOnlyCollection<DispatchLineDetail> Lines, IReadOnlyCollection<DispatchShortageDetail> Shortages, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string RowVersion, DispatchMutationResult? Summary = null, DispatchDetailQuery? Pagination = null);

public sealed record DispatchDeliveryPaymentInput(string ApplicationType, string? PaymentMethod, decimal Amount, string? Reference, string? EvidenceUrl,
    Guid? PaymentMethodId = null, int? PaymentMethodVersion = null, string? CardFranchiseCode = null, string? ApprovalNumber = null);
public sealed record VerifyDispatchPaymentRequest(bool IsVerified, DateTimeOffset? ExpectedCorrectedAt = null);
public sealed record CorrectDispatchPaymentRequest(Guid PaymentMethodId, int PaymentMethodVersion,
    string MethodCode, string? Reference, string? CardFranchiseCode, string? ApprovalNumber, string Reason,
    DateTimeOffset? ExpectedCorrectedAt = null);
public sealed record DispatchReasonOption(Guid DispatchReasonId, string ReasonType, string Code, string Name);
public sealed record DispatchDeliveryReturnInput(int OriginalLineNumber, decimal Quantity, string InventoryDisposition, string ReasonCode, string ReasonDescription);
public sealed record RecordDispatchDeliveryRequest(Guid DispatchSourceDocumentId, string DeliveryStatus, string? Reason, string? Notes, decimal? Latitude, decimal? Longitude, DateTimeOffset OccurredAt, string IdempotencyKey, IReadOnlyCollection<DispatchDeliveryPaymentInput> Payments, IReadOnlyCollection<DispatchDeliveryReturnInput> Returns);
public sealed record ReorderDispatchDocumentsRequest(IReadOnlyCollection<Guid> OrderedDocumentIds, string RowVersion);
public sealed record DispatchExpenseInput(string Category, decimal Amount, string? Description, string? EvidenceUrl, string IdempotencyKey, DateTimeOffset OccurredAt, Guid? ConceptId = null);
public sealed record ReviewDispatchExpenseRequest(string Decision, decimal? ApprovedAmount, string? Notes, string IdempotencyKey);
public sealed record CloseDispatchRouteRequest(decimal DeclaredCash, string? DifferenceReason, string IdempotencyKey,
    decimal? DeclaredCard = null, decimal? DeclaredTransfer = null);
public sealed record DispatchClosureReceiptRequest(string? CompanyName = null, int PaperWidthMillimeters = 80);
public sealed record DispatchClosureReceiptView(string Html);
public sealed record SettleDispatchRequest(
    decimal CashReceived,
    string? Notes,
    string IdempotencyKey,
    Guid? CashDifferencePaymentMethodId = null,
    IReadOnlyList<Guid>? ReturnIds = null);

public sealed record DispatchDeliveryPaymentDetail(Guid PaymentId, string ApplicationType, string? PaymentMethod, decimal Amount, string? Reference, string? EvidenceUrl,
    Guid? PaymentMethodId = null, int? PaymentMethodVersion = null, string? FormName = null,
    string? CardFranchiseCode = null, string? ApprovalNumber = null, Guid? VerifiedBy = null,
    DateTimeOffset? VerifiedAt = null, string? OriginalPaymentMethod = null, string? OriginalFormName = null,
    string? CorrectionReason = null, Guid? CorrectedBy = null, DateTimeOffset? CorrectedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EvidenceReadUrl = null);
public sealed record DispatchPaymentReviewResult(Guid DispatchId, Guid DispatchSourceDocumentId,
    DispatchDeliveryPaymentDetail Payment, decimal GrossCash, decimal ExpectedCash,
    decimal CashDifference, decimal TransferTotal, decimal CardTotal);
public sealed record DispatchDeliveryReturnDetail(Guid ReturnLineId, int OriginalLineNumber, Guid ProductId, string ProductCode, string Description, decimal Quantity, string InventoryDisposition, string ReasonCode, string ReasonDescription);
public sealed record DispatchDeliveryProductLine(int OriginalLineNumber, Guid ProductId, string ProductCode, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal ReturnedQuantity = 0, decimal ReturnedLineTotal = 0);
public sealed record DispatchDeliveryDocument(Guid DispatchSourceDocumentId, Guid SourceDocumentId, string DocumentType, string DocumentNumber, int Sequence, Guid? CustomerId, string CustomerName, string? DeliveryAddress, decimal DocumentTotal, decimal CreditAmount, decimal? DestinationLatitude, decimal? DestinationLongitude, string DeliveryStatus, string? Reason, string? Notes, decimal? Latitude, decimal? Longitude, DateTimeOffset? DeliveredAt, IReadOnlyCollection<DispatchDeliveryProductLine> Lines, IReadOnlyCollection<DispatchDeliveryPaymentDetail> Payments, IReadOnlyCollection<DispatchDeliveryReturnDetail> Returns, decimal ReturnTotal = 0, DispatchReturnCalculation? ReturnCalculation = null, string CollectionMode = "Session", decimal? CreditCollectibleAmount = null, decimal? UncollectedPaymentAmount = null)
{
    public string? FiscalNumber { get; init; }
}
public sealed record DispatchReturnCalculation(decimal OriginalUnrounded,decimal OriginalRounding,decimal UnroundedOutstanding,decimal RemainingRounding);
public sealed record DispatchExpenseDetail(Guid ExpenseId, string Category, decimal Amount, string? Description, string? EvidenceUrl, string ApprovalStatus, decimal? ApprovedAmount,
    Guid? ConceptId = null, Guid? AcceptedExpenseId = null, DateTimeOffset? OccurredAt = null,string? AcceptedExpenseStatus = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EvidenceReadUrl = null);
public sealed record DispatchSettlementSummary(decimal GrossCash, decimal ApprovedCashExpenses, decimal ExpectedCash, decimal DeclaredCash, decimal Difference, decimal DepositTotal, decimal RemainingCreditTotal, decimal CreditAdvanceTotal, decimal ReturnTotal, decimal UndeliveredTotal, decimal DispatchTotal, decimal BalanceDifference, string Status, Guid? ReceivedBy, DateTimeOffset? ReceivedAt, decimal? CashReceived = null, decimal CardTotal = 0,
    decimal? DeclaredCard = null, decimal? DeclaredTransfer = null, Guid? TransporterClosedBy = null, DateTimeOffset? TransporterClosedAt = null, decimal PreviouslyCollectedTotal = 0,
    int ReceiptTemplateVersion = 1)
{
    public const int CurrentReceiptTemplateVersion = 2;
}
public sealed record DispatchLinkedReturn(Guid ReturnId,Guid OriginalDocumentId,string DocumentNumber,decimal TotalAmount,string EconomicResolution);
public sealed record DispatchExecutionDetail(Guid DispatchId, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, string Status, IReadOnlyCollection<DispatchDeliveryDocument> Documents, IReadOnlyCollection<DispatchExpenseDetail> Expenses, DispatchSettlementSummary? Settlement, string? RowVersion = null,
    IReadOnlyCollection<DispatchLinkedReturn>? LinkedReturns = null);

public sealed record DispatchReportRow(string DispatchNumber, DateOnly ScheduledDate, string Status, string DriverName, string? VehiclePlate, string DocumentType, string DocumentNumber, string CustomerName, string? DeliveryAddress, string SellerName, string ProductCode, string ProductName, decimal AssignedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, decimal? UnitPrice, decimal? LineTotal)
{
    public string? FiscalNumber { get; init; }
}
public sealed record DispatchReport(string Title, DateTimeOffset GeneratedAt, bool IncludesPrices, IReadOnlyCollection<DispatchReportRow> Rows);

public sealed class DispatchValidationException(string message) : Exception(message);
public sealed class DispatchForbiddenException(string message) : Exception(message);
public sealed class DispatchNotFoundException(string message) : Exception(message);
public sealed class DispatchConflictException(string message) : Exception(message);
