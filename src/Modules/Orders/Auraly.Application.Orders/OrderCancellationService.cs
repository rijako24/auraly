using Auraly.Contracts.Orders;

namespace Auraly.Application.Orders;

public sealed record StoredOrderCancellation(
    Guid OrderId,
    string OrderNumber,
    bool IsReplay);

public interface IOrderCancellationStore
{
    Task<StoredOrderCancellation> CancelAsync(
        OrderActor actor,
        Guid orderId,
        Guid? workSessionId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class OrderCancellationService(IOrderCancellationStore cancellations)
{
    public async Task<CancelOrderResponse> CancelAsync(
        OrderActor actor,
        Guid orderId,
        CancelOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        if (!actor.Permissions.Contains(OrderPermissionCodes.Cancel))
            throw new OrderForbiddenException(
                $"Permission '{OrderPermissionCodes.Cancel}' is required.");
        return await CancelAuthorizedAsync(
            actor, orderId, request, idempotencyKey, cancellationToken);
    }

    public async Task<CancelOrderResponse> CancelAuthorizedAsync(
        OrderActor actor,
        Guid orderId,
        CancelOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        if (orderId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > 500 || string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Trim().Length > 160)
            throw new OrderValidationException(
                "El pedido, el motivo y la clave idempotente son obligatorios.");
        if (request.WorkSessionId is not null &&
            request.WorkSessionId != actor.WorkSessionId)
            throw new OrderForbiddenException(
                "La sesión solicitada no coincide con el dispositivo autenticado.");

        var result = await cancellations.CancelAsync(
            actor,
            orderId,
            request.WorkSessionId,
            request.Reason.Trim(),
            idempotencyKey.Trim(),
            cancellationToken);
        return new(
            result.OrderId,
            result.OrderNumber,
            "Cancelled",
            result.IsReplay);
    }
}
