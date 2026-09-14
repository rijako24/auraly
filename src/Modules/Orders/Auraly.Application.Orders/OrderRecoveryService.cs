using Auraly.Application.Sales;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Orders;

public sealed record PreparedOrderRecovery(
    OrderDetail Order,
    bool ClaimAcquiredByThisAttempt);

public sealed class OrderRecoveryService(
    OrderService orders,
    OnlineSalesOrderImportService sales,
    OnlineSalesCheckoutService checkout)
{
    public async Task<RecoveredOrderSale> RecoverAsync(
        OrderActor actor,
        Guid orderId,
        RecoverOrderIntoSaleRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request.DraftId == Guid.Empty || request.ExpectedDraftVersion < 1)
            throw new OrderValidationException(
                "La venta activa y su versión son obligatorias.");

        var prepared = await PrepareAsync(
            actor,
            orderId,
            request.WorkSessionId,
            request.UserId,
            cancellationToken);
        var order = prepared.Order;
        var importCompleted = false;
        try
        {
            var draft = await sales.ImportAsync(
                new OnlineSalesUserIdentity(
                    actor.UserId,
                    actor.TenantId,
                    actor.Permissions),
                request.DraftId,
                new ImportOnlineSalesOrderRequest(
                    order.OrderId,
                    order.OrderNumber,
                    order.CustomerId,
                    order.Lines.Select(line => new OnlineSalesOrderImportLine(
                        line.ProductId!.Value,
                        line.Quantity,
                        line.UnitPrice,
                        line.DiscountAmount,
                        line.PriceSource,
                        line.DocumentUnitCost)).ToArray(),
                    request.ExpectedDraftVersion),
                idempotencyKey,
                cancellationToken);
            importCompleted = true;
            return new RecoveredOrderSale(
                order.OrderId,
                draft.DraftId,
                draft.Version,
                order.OrderNumber,
                draft.PayableAmount);
        }
        catch when (!importCompleted && prepared.ClaimAcquiredByThisAttempt)
        {
            await orders.ReleaseClaimAsync(
                actor,
                orderId,
                new ReleaseOrderClaimRequest(request.WorkSessionId, request.UserId),
                cancellationToken);
            throw;
        }
    }

    public async Task<PreparedOrderRecovery> PrepareAsync(
        OrderActor actor,
        Guid orderId,
        Guid workSessionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (workSessionId == Guid.Empty || userId == Guid.Empty)
            throw new OrderValidationException(
                "La sesión de trabajo y el usuario son obligatorios.");

        var order = await orders.GetAsync(actor, orderId, cancellationToken);
        var canEditReview = order.Status == "InReview" &&
            (actor.Permissions.Contains(OrderPermissionCodes.Update) ||
             actor.Permissions.Contains(OrderPermissionCodes.Review));
        if (!order.CanInvoice && !canEditReview)
            throw new OrderConflictException(
                "El pedido no está disponible para facturar o corregir.");
        if (order.WarehouseId is null)
            throw new OrderConflictException(
                "El pedido no tiene una bodega de venta asignada y no puede recuperarse.");
        if (order.Lines.Any(line => line.ProductId is null))
            throw new OrderConflictException(
                "El pedido tiene productos sin equivalencia en el catálogo de Auraly.");

        var targetWasAlreadyClaimedBySession =
            order.Claim?.IsOwnedByCurrentActor == true;
        var claimAcquiredByThisAttempt = !targetWasAlreadyClaimedBySession;
        try
        {
            await orders.ClaimAsync(
                actor,
                orderId,
                new ClaimOrderRequest(workSessionId, userId),
                cancellationToken);
            if (order.Source == 1)
            {
                await checkout.PrepareSourceOrderInventoryAsync(
                    new OnlineSalesUserIdentity(
                        actor.UserId,
                        actor.TenantId,
                        actor.Permissions),
                    order.BusinessId,
                    order.OrderId,
                    order.WarehouseId.Value,
                    cancellationToken);
            }
            await orders.ReleaseOtherClaimsAsync(
                actor,
                orderId,
                workSessionId,
                userId,
                cancellationToken);
            return new PreparedOrderRecovery(order, claimAcquiredByThisAttempt);
        }
        catch when (claimAcquiredByThisAttempt)
        {
            await orders.ReleaseClaimAsync(
                actor,
                orderId,
                new ReleaseOrderClaimRequest(workSessionId, userId),
                cancellationToken);
            throw;
        }
    }
}
