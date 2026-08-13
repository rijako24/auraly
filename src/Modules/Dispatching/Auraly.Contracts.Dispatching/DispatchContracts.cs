namespace Auraly.Contracts.Dispatching;

public static class DispatchPermissionCodes
{
    public const string Read = "dispatches.read";
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
    public const string Cancelled = "Cancelled";
}

public sealed record DispatchActorIdentity(Guid UserId, Guid TenantId, Guid BusinessId, IReadOnlySet<string> Permissions);
public sealed record DispatchQuery(int Page = 1, int PageSize = 25, string? Search = null, string? Status = null, DateOnly? From = null, DateOnly? To = null);
public sealed record DispatchListItem(Guid DispatchId, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, string Status, int DocumentCount, int LineCount, decimal ExpectedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, DateTimeOffset UpdatedAt, string RowVersion);
public sealed record DispatchPage(IReadOnlyCollection<DispatchListItem> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record DispatchWarehouseOption(Guid WarehouseId, string Code, string Name);
public sealed record DispatchRouteOption(Guid RouteId, string Code, string Name, string SellerName);
public sealed record DispatchOptions(IReadOnlyCollection<DispatchWarehouseOption> Warehouses, IReadOnlyCollection<DispatchRouteOption> Routes);

public sealed record DispatchCandidateQuery(int Page = 1, int PageSize = 50, string? Search = null, string? DocumentType = null, DateOnly? From = null, DateOnly? To = null, Guid? WarehouseId = null);
public sealed record DispatchCandidateDocument(Guid DocumentId, string DocumentType, string DocumentNumber, DateTimeOffset IssuedAt, Guid WarehouseId, string WarehouseName, Guid? CustomerId, string CustomerName, string? DeliveryAddress, string SellerName, int LineCount, decimal PendingQuantity, decimal DocumentTotal);
public sealed record DispatchCandidatePage(IReadOnlyCollection<DispatchCandidateDocument> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record CreateDispatchRequest(Guid BusinessId, Guid WarehouseId, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, Guid? RouteId, string? Notes, IReadOnlyCollection<Guid> SourceDocumentIds);
public sealed record AddDispatchDocumentsRequest(IReadOnlyCollection<Guid> SourceDocumentIds, string RowVersion);
public sealed record DispatchTransitionRequest(string RowVersion, string IdempotencyKey);
public sealed record DispatchVerificationRequest(Guid DispatchLineId, decimal QuantityDelta, string? Barcode, string IdempotencyKey, DateTimeOffset OccurredAt);
public sealed record DeclareDispatchShortageRequest(Guid DispatchLineId, decimal Quantity, string Reason, string? Notes, string RowVersion, string IdempotencyKey);

public sealed record DispatchMutationResult(Guid DispatchId, string DispatchNumber, string Status, int DocumentCount, int LineCount, decimal ExpectedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, string RowVersion);
public sealed record DispatchDocumentDetail(Guid DispatchSourceDocumentId, Guid SourceDocumentId, string DocumentType, string DocumentNumber, Guid? CustomerId, string CustomerName, string? DeliveryAddress, string SellerName, decimal DocumentTotal, string Status);
public sealed record DispatchLineDetail(Guid DispatchLineId, Guid DispatchSourceDocumentId, int SourceLineNumber, Guid ProductId, string ProductCode, string Description, decimal AssignedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, string Status, string RowVersion);
public sealed record DispatchShortageDetail(Guid DispatchShortageId, Guid DispatchLineId, Guid ProductId, string ProductCode, string Description, decimal Quantity, string Reason, string? Notes, DateTimeOffset CreatedAt);
public sealed record DispatchDetail(Guid DispatchId, Guid BusinessId, Guid WarehouseId, string WarehouseName, string DispatchNumber, DateOnly ScheduledDate, string DriverName, string? VehiclePlate, Guid? RouteId, string? RouteName, string? Notes, string Status, IReadOnlyCollection<DispatchDocumentDetail> Documents, IReadOnlyCollection<DispatchLineDetail> Lines, IReadOnlyCollection<DispatchShortageDetail> Shortages, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string RowVersion);

public sealed record DispatchReportRow(string DispatchNumber, DateOnly ScheduledDate, string Status, string DriverName, string? VehiclePlate, string DocumentType, string DocumentNumber, string CustomerName, string? DeliveryAddress, string SellerName, string ProductCode, string ProductName, decimal AssignedQuantity, decimal VerifiedQuantity, decimal ShortageQuantity, decimal? UnitPrice, decimal? LineTotal);
public sealed record DispatchReport(string Title, DateTimeOffset GeneratedAt, bool IncludesPrices, IReadOnlyCollection<DispatchReportRow> Rows);

public sealed class DispatchValidationException(string message) : Exception(message);
public sealed class DispatchForbiddenException(string message) : Exception(message);
public sealed class DispatchNotFoundException(string message) : Exception(message);
public sealed class DispatchConflictException(string message) : Exception(message);
