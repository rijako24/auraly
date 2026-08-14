namespace Auraly.Platform.Application.Commerce;

public sealed record UnavailableOrderItem(
    Guid OrderItemId,
    Guid? ProductId,
    string? Sku,
    string ProductName,
    string Reason);
