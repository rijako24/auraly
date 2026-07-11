using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Commerce;

public sealed record AddOrderItemRequest(
    Guid? ProductId,
    string? ExternalProductId,
    string? Sku,
    string? Name,
    decimal Quantity,
    decimal? UnitPrice);

public sealed record CreateOrderRequest(
    bool CustomerConfirmed,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? CustomerDocument,
    string? DeliveryAddress,
    string? Notes);

public sealed record OrderItemSnapshot(
    Guid OrderItemId,
    Guid? ProductId,
    string? ExternalProductId,
    string? Sku,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderSnapshot(
    Guid OrderId,
    OrderStatus Status,
    string Currency,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    IReadOnlyList<OrderItemSnapshot> Items,
    Guid? PaymentTransactionId = null,
    string? ExternalOrderId = null,
    string? ExternalDocumentNumber = null,
    string? ExternalStatus = null);

public sealed record CreateExternalOrderResult(
    string ExternalOrderId,
    string? ExternalDocumentNumber,
    string? ExternalStatus,
    string ResponseJson);
public sealed class InsufficientProductStockException : InvalidOperationException
{
    public InsufficientProductStockException(
        string productName,
        decimal requestedQuantity,
        decimal availableQuantity,
        decimal existingCartQuantity)
        : base("Insufficient product stock.")
    {
        ProductName = productName;
        RequestedQuantity = requestedQuantity;
        AvailableQuantity = availableQuantity;
        ExistingCartQuantity = existingCartQuantity;
    }

    public string ProductName { get; }
    public decimal RequestedQuantity { get; }
    public decimal AvailableQuantity { get; }
    public decimal ExistingCartQuantity { get; }
    public decimal AvailableToAdd => Math.Max(AvailableQuantity - ExistingCartQuantity, 0m);
}
