namespace Auraly.Contracts.Orders;

public static class OrderPermissionCodes
{
    public const string Read = "orders.read";
    public const string Recover = "orders.recover";
    public const string Invoice = "orders.invoice";
    public const string Cancel = "orders.cancel";
    public const string OverridePricing = "orders.override-pricing";
}

public sealed record OrderPageRequest(
    int Page = 1,
    int PageSize = 50,
    string? OrderNumber = null,
    string? Customer = null,
    string? Product = null,
    string? Status = null,
    int? Source = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    bool? HasPendingBalance = null,
    bool IncludeClaimedByOthers = true);

public sealed record OrderClaimSummary(
    Guid ClaimId,
    Guid WorkSessionId,
    Guid? DeviceId,
    Guid UserId,
    DateTimeOffset ExpiresAt,
    bool IsOwnedByCurrentActor);

public sealed record OrderListItem(
    Guid OrderId,
    string OrderNumber,
    string Status,
    int Source,
    string? CustomerName,
    string? CustomerIdentification,
    string? CustomerPhone,
    string Currency,
    decimal Total,
    int LineCount,
    DateTimeOffset CreatedAt,
    bool CanInvoice,
    Guid? InvoiceDocumentId,
    OrderClaimSummary? Claim);

public sealed record OrderPage(
    IReadOnlyList<OrderListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore);

public sealed record OrderLine(
    Guid OrderItemId,
    Guid? ProductId,
    string? ProductCode,
    string? Sku,
    string ProductName,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal);

public sealed record OrderDetail(
    Guid OrderId,
    Guid BusinessId,
    string OrderNumber,
    string Status,
    int Source,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerIdentification,
    string? CustomerPhone,
    string? CustomerEmail,
    string? DeliveryAddress,
    string? Notes,
    string Currency,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Total,
    Guid? PaymentTransactionId,
    string? PaymentStatus,
    DateTimeOffset CreatedAt,
    bool CanInvoice,
    Guid? InvoiceDocumentId,
    OrderClaimSummary? Claim,
    IReadOnlyList<OrderLine> Lines);

public sealed record ClaimOrderRequest(
    Guid WorkSessionId,
    Guid UserId,
    int LeaseMinutes = 10);

public sealed record ReleaseOrderClaimRequest(
    Guid WorkSessionId,
    Guid UserId);
