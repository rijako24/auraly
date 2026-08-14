namespace Auraly.Contracts.Dispatching;

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
}

public sealed record DispatchActorIdentity(Guid UserId, Guid TenantId, Guid BusinessId, IReadOnlySet<string> Permissions);
public sealed record DispatchQuery(int Page = 1, int PageSize = 25, string? Search = null, string? Status = null, DateOnly? From = null, DateOnly? To = null);
public sealed record DispatchListItem(Guid DispatchId, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, string Status, int DocumentCount, int LineCount, decimal ExpectedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, DateTimeOffset UpdatedAt, string RowVersion);
public sealed record DispatchPage(IReadOnlyCollection<DispatchListItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record DispatchWarehouseOption(Guid WarehouseId, string Code, string Name);
public sealed record DispatchRouteOption(Guid RouteId, string Code, string Name, string SellerName);
public sealed record DispatchDriverOption(Guid UserId, string Name);
public sealed record DispatchOptions(IReadOnlyCollection<DispatchWarehouseOption> Warehouses, IReadOnlyCollection<DispatchRouteOption> Routes, IReadOnlyCollection<DispatchDriverOption> Drivers);

public sealed record DispatchCandidateQuery(int Page = 1, int PageSize = 50, string? Search = null, string? DocumentType = null, DateOnly? From = null, DateOnly? To = null, Guid? WarehouseId = null);
public sealed record DispatchCandidateDocument(Guid DocumentId, string DocumentType, string DocumentNumber, DateTimeOffset IssuedAt, Guid WarehouseId, string WarehouseName, Guid? CustomerId, string CustomerName, string? DeliveryAddress, string SellerName, int LineCount, decimal PendingQuantity, decimal DocumentTotal);
public sealed record DispatchCandidatePage(IReadOnlyCollection<DispatchCandidateDocument> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record CreateDispatchRequest(Guid BusinessId, Guid WarehouseId, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, Guid? DriverUserId, Guid? RouteId, string? Notes, IReadOnlyCollection<Guid> SourceDocumentIds);
public sealed record AddDispatchDocumentsRequest(IReadOnlyCollection<Guid> SourceDocumentIds, string RowVersion);
public sealed record DispatchTransitionRequest(string RowVersion, string IdempotencyKey);
public sealed record DispatchVerificationRequest(Guid DispatchLineId, decimal QuantityDelta, string? Barcode, string IdempotencyKey, DateTimeOffset OccurredAt);
public sealed record DeclareDispatchShortageRequest(Guid DispatchLineId, decimal Quantity, string Reason, string? Notes, string RowVersion, string IdempotencyKey);

public sealed record DispatchMutationResult(Guid DispatchId, string DispatchNumber, string Status, int DocumentCount, int LineCount, decimal ExpectedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, string RowVersion);
public sealed record DispatchDocumentDetail(Guid DispatchSourceDocumentId, Guid SourceDocumentId, string DocumentType, string DocumentNumber, Guid? CustomerId, string CustomerName, string? DeliveryAddress, string SellerName, decimal DocumentTotal, string Status);
public sealed record DispatchLineDetail(Guid DispatchLineId, Guid DispatchSourceDocumentId, int SourceLineNumber, Guid ProductId, string ProductCode, string Description, decimal AssignedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, string Status, string RowVersion);
public sealed record DispatchShortageDetail(Guid DispatchShortageId, Guid DispatchLineId, Guid ProductId, string ProductCode, string Description, decimal Quantity, string Reason, string? Notes, DateTimeOffset CreatedAt);
public sealed record DispatchDetail(Guid DispatchId, Guid BusinessId, Guid WarehouseId, string WarehouseName, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, Guid? RouteId, string? RouteName, string? Notes, string Status, IReadOnlyCollection<DispatchDocumentDetail> Documents, IReadOnlyCollection<DispatchLineDetail> Lines, IReadOnlyCollection<DispatchShortageDetail> Shortages, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string RowVersion);

public sealed record DispatchDeliveryPaymentInput(string ApplicationType, string? PaymentMethod, decimal Amount, string? Reference, string? EvidenceUrl);
public sealed record DispatchDeliveryReturnInput(int OriginalLineNumber, decimal Quantity, string InventoryDisposition, string ReasonCode, string ReasonDescription);
public sealed record RecordDispatchDeliveryRequest(Guid DispatchSourceDocumentId, string DeliveryStatus, string? Reason, string? Notes, decimal? Latitude, decimal? Longitude, DateTimeOffset OccurredAt, string IdempotencyKey, IReadOnlyCollection<DispatchDeliveryPaymentInput> Payments, IReadOnlyCollection<DispatchDeliveryReturnInput> Returns);
public sealed record ReorderDispatchDocumentsRequest(IReadOnlyCollection<Guid> OrderedDocumentIds, string RowVersion, string IdempotencyKey);
public sealed record DispatchExpenseInput(string Category, decimal Amount, string Description, string EvidenceUrl, string IdempotencyKey, DateTimeOffset OccurredAt);
public sealed record ReviewDispatchExpenseRequest(string Decision, decimal? ApprovedAmount, string? Notes, string IdempotencyKey);
public sealed record CloseDispatchRouteRequest(decimal DeclaredCash, string? DifferenceReason, string IdempotencyKey);
public sealed record SettleDispatchRequest(decimal CashReceived, string? Notes, string IdempotencyKey);

public sealed record DispatchDeliveryPaymentDetail(Guid PaymentId, string ApplicationType, string? PaymentMethod, decimal Amount, string? Reference, string? EvidenceUrl);
public sealed record DispatchDeliveryReturnDetail(Guid ReturnLineId, int OriginalLineNumber, Guid ProductId, string ProductCode, string Description, decimal Quantity, string InventoryDisposition, string ReasonCode, string ReasonDescription);
public sealed record DispatchDeliveryProductLine(int OriginalLineNumber, Guid ProductId, string ProductCode, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record DispatchDeliveryDocument(Guid DispatchSourceDocumentId, Guid SourceDocumentId, string DocumentType, string DocumentNumber, int Sequence, Guid? CustomerId, string CustomerName, string? DeliveryAddress, decimal DocumentTotal, decimal CreditAmount, string DeliveryStatus, string? Reason, string? Notes, decimal? Latitude, decimal? Longitude, DateTimeOffset? DeliveredAt, IReadOnlyCollection<DispatchDeliveryProductLine> Lines, IReadOnlyCollection<DispatchDeliveryPaymentDetail> Payments, IReadOnlyCollection<DispatchDeliveryReturnDetail> Returns);
public sealed record DispatchExpenseDetail(Guid ExpenseId, string Category, decimal Amount, string Description, string EvidenceUrl, string ApprovalStatus, decimal? ApprovedAmount);
public sealed record DispatchSettlementSummary(decimal GrossCash, decimal ApprovedCashExpenses, decimal ExpectedCash, decimal DeclaredCash, decimal Difference, decimal DepositTotal, decimal RemainingCreditTotal, decimal CreditAdvanceTotal, decimal ReturnTotal, decimal UndeliveredTotal, decimal DispatchTotal, decimal BalanceDifference, string Status, Guid? ReceivedBy, DateTimeOffset? ReceivedAt);
public sealed record DispatchExecutionDetail(Guid DispatchId, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, string Status, IReadOnlyCollection<DispatchDeliveryDocument> Documents, IReadOnlyCollection<DispatchExpenseDetail> Expenses, DispatchSettlementSummary? Settlement);

public sealed record DispatchReportRow(string DispatchNumber, DateOnly ScheduledDate, string Status, string DriverName, string? VehiclePlate, string DocumentType, string DocumentNumber, string CustomerName, string? DeliveryAddress, string SellerName, string ProductCode, string ProductName, decimal AssignedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, decimal? UnitPrice, decimal? LineTotal);
public sealed record DispatchReport(string Title, DateTimeOffset GeneratedAt, bool IncludesPrices, IReadOnlyCollection<DispatchReportRow> Rows);

public sealed class DispatchValidationException(string message) : Exception(message);
public sealed class DispatchForbiddenException(string message) : Exception(message);
public sealed class DispatchNotFoundException(string message) : Exception(message);
public sealed class DispatchConflictException(string message) : Exception(message);
