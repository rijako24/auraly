namespace Auraly.Contracts.Purchasing;

public static class PurchaseOrderStatuses
{
    public const string Draft = "Draft";
    public const string Open = "Open";
    public const string PartiallyReceived = "PartiallyReceived";
    public const string Received = "Received";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

public sealed record PurchaseOrderLineRequest(
    Guid LineId, int LineNumber, Guid ProductId, string Description,
    decimal OrderedQuantity, decimal UnitCost, decimal DiscountAmount,
    string TaxCode, decimal TaxRate, string TaxTreatment,
    string PresentationName = "Unidad", decimal PresentationQuantity = 1,
    decimal UnitsPerPresentation = 1);

public sealed record SavePurchaseOrderDraftRequest(
    Guid PurchaseOrderId, Guid BusinessId, Guid? WarehouseId, Guid? SupplierId,
    DateTimeOffset OrderedAt, DateTimeOffset? ExpectedAt, string CurrencyCode,
    string? Notes, IReadOnlyCollection<PurchaseOrderLineRequest> Lines,
    string? ConcurrencyToken);

public sealed record ConfirmPurchaseOrderRequest(
    Guid PurchaseOrderId, Guid BusinessId, Guid WarehouseId, Guid SupplierId,
    DateTimeOffset OrderedAt, DateTimeOffset? ExpectedAt, string CurrencyCode,
    string? Notes, IReadOnlyCollection<PurchaseOrderLineRequest> Lines,
    string? DraftConcurrencyToken);

public sealed record PurchaseOrderLine(
    Guid LineId, int LineNumber, Guid ProductId, string ProductCode, string Description,
    decimal OrderedQuantity, decimal ReceivedQuantity, decimal CancelledQuantity,
    decimal RemainingQuantity, decimal UnitCost, decimal DiscountAmount,
    string TaxCode, decimal TaxRate, string TaxTreatment, decimal NetAmount,
    decimal TaxAmount, decimal LineTotal, string PresentationName,
    decimal PresentationQuantity, decimal UnitsPerPresentation,
    decimal Rotation30Days, decimal Rotation90Days, decimal DailyDemand90Days,
    decimal CurrentStock, decimal IncomingQuantity, DateTimeOffset? RotationCalculatedAt);

public sealed record PurchaseOrderDetail(
    Guid PurchaseOrderId, string? DocumentNumber, string Status,
    Guid? WarehouseId, string? WarehouseName, Guid? SupplierId, string? SupplierName,
    DateTimeOffset OrderedAt, DateTimeOffset? ExpectedAt, string CurrencyCode,
    string? Notes, decimal NetAmount, decimal TaxAmount, decimal GrandTotal,
    DateTimeOffset UpdatedAt, string? ConcurrencyToken,
    IReadOnlyList<PurchaseOrderLine> Lines);

public sealed record PurchaseOrderListItem(
    Guid PurchaseOrderId, string? DocumentNumber, string Status,
    string? SupplierName, string? WarehouseName, DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt, decimal GrandTotal, decimal FulfillmentPercent,
    DateTimeOffset UpdatedAt);

public sealed record PurchaseOrderPage(
    IReadOnlyList<PurchaseOrderListItem> Items, int Page, int PageSize,
    int TotalCount, int TotalPages);

public sealed record PurchaseOrderConfirmation(
    Guid PurchaseOrderId, string DocumentNumber, string Status, bool IdempotentReplay);

public sealed record PurchaseOrderSuggestionRequest(
    Guid BusinessId, Guid WarehouseId, Guid SupplierId,
    IReadOnlyCollection<Guid> ProductIds, int TargetCoverageDays = 7);

public sealed record PurchaseOrderSuggestion(
    Guid ProductId, int TargetCoverageDays, decimal Rotation30Days,
    decimal Rotation90Days, decimal DailyDemand90Days, decimal ForecastDailyDemand,
    decimal CurrentStock,
    decimal IncomingQuantity, string PresentationName, decimal UnitsPerPresentation,
    decimal SuggestedQuantity, decimal SuggestedPresentationQuantity,
    DateTimeOffset? RotationCalculatedAt);

public sealed record ClosePurchaseOrderRequest(string Reason, string ConcurrencyToken);

public sealed record PurchaseOrderReceiptSource(
    Guid PurchaseOrderId, string DocumentNumber, string Status,
    Guid WarehouseId, Guid SupplierId, DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt, string CurrencyCode, string? Notes,
    IReadOnlyList<PurchaseOrderLine> Lines);
