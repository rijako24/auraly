using Auraly.Contracts.Orders;
using Auraly.Domain.Orders;

namespace Auraly.Application.Orders;

public sealed record OrderActor(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    Guid? WorkSessionId,
    Guid? DeviceId,
    IReadOnlySet<string> Permissions);

public interface IOrderStore
{
    Task<OrderPage> PageAsync(
        OrderActor actor,
        OrderPageRequest request,
        CancellationToken cancellationToken);

    Task<OrderDetail?> GetAsync(
        OrderActor actor,
        Guid orderId,
        CancellationToken cancellationToken);

    Task<OrderClaimSummary> ClaimAsync(
        OrderActor actor,
        Guid orderId,
        Guid workSessionId,
        int leaseMinutes,
        CancellationToken cancellationToken);

    Task ReleaseClaimAsync(
        OrderActor actor,
        Guid orderId,
        Guid workSessionId,
        CancellationToken cancellationToken);

    Task ReleaseOtherClaimsAsync(
        OrderActor actor,
        Guid retainedOrderId,
        Guid workSessionId,
        CancellationToken cancellationToken);
}

public sealed class OrderService(IOrderStore orders)
{
    public Task<OrderPage> PageAsync(
        OrderActor actor,
        OrderPageRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(actor, OrderPermissionCodes.Read);
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new OrderValidationException(
                "La página y el tamaño solicitado no son válidos.");
        if (request.CreatedFrom > request.CreatedTo)
            throw new OrderValidationException(
                "La fecha inicial no puede superar la fecha final.");
        ValidateText(request.OrderNumber, 120);
        ValidateText(request.Customer, 160);
        ValidateText(request.Product, 160);
        ValidateText(request.Status, 32);
        return orders.PageAsync(actor, request, cancellationToken);
    }

    public async Task<OrderDetail> GetAsync(
        OrderActor actor,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        Demand(actor, OrderPermissionCodes.Read);
        if (orderId == Guid.Empty)
            throw new OrderValidationException("El pedido es obligatorio.");
        return await orders.GetAsync(actor, orderId, cancellationToken)
            ?? throw new OrderNotFoundException("El pedido no existe en esta sede.");
    }

    public Task<OrderClaimSummary> ClaimAsync(
        OrderActor actor,
        Guid orderId,
        ClaimOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(actor, OrderPermissionCodes.Recover);
        ValidateActorRequest(actor, orderId, request.WorkSessionId, request.UserId);
        return orders.ClaimAsync(
            actor,
            orderId,
            request.WorkSessionId,
            OrderRules.LeaseMinutes(request.LeaseMinutes),
            cancellationToken);
    }

    public Task ReleaseClaimAsync(
        OrderActor actor,
        Guid orderId,
        ReleaseOrderClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(actor, OrderPermissionCodes.Recover);
        ValidateActorRequest(actor, orderId, request.WorkSessionId, request.UserId);
        return orders.ReleaseClaimAsync(
            actor,
            orderId,
            request.WorkSessionId,
            cancellationToken);
    }

    public Task ReleaseOtherClaimsAsync(
        OrderActor actor,
        Guid retainedOrderId,
        Guid workSessionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        Demand(actor, OrderPermissionCodes.Recover);
        ValidateActorRequest(actor, retainedOrderId, workSessionId, userId);
        return orders.ReleaseOtherClaimsAsync(
            actor,
            retainedOrderId,
            workSessionId,
            cancellationToken);
    }

    private static void ValidateActorRequest(
        OrderActor actor,
        Guid orderId,
        Guid workSessionId,
        Guid userId)
    {
        if (orderId == Guid.Empty || workSessionId == Guid.Empty ||
            userId == Guid.Empty || userId != actor.UserId)
            throw new OrderValidationException(
                "Pedido, sesión y usuario autenticado son obligatorios.");
        if (actor.WorkSessionId is not null && actor.WorkSessionId != workSessionId)
            throw new OrderForbiddenException(
                "La sesión solicitada no coincide con el dispositivo autenticado.");
    }

    private static void Demand(OrderActor actor, string permission)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.Permissions.Contains(permission))
            throw new OrderForbiddenException(
                $"Permission '{permission}' is required.");
    }

    private static void ValidateText(string? value, int maximum)
    {
        if (value?.Length > maximum)
            throw new OrderValidationException(
                $"Uno de los filtros supera {maximum} caracteres.");
    }
}

public sealed class OrderForbiddenException(string message) : Exception(message);
public sealed class OrderValidationException(string message) : Exception(message);
public sealed class OrderConflictException(string message) : Exception(message);
public sealed class OrderNotFoundException(string message) : Exception(message);
