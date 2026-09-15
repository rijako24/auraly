using Auraly.Application.Sales;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Orders;

public sealed record PreparedOrderRecovery(
    OrderDetail Order,
    bool ClaimAcquiredByThisAttempt);

public sealed class OrderRecoveryService(
    OrderService orders,
    OnlineSalesOrderImportService sales)
{
    public async Task<RecoveredOrderSale> RecoverAsync(
        OrderActor actor,
        Guid orderId,
        RecoverOrderIntoSaleRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var prepared = await PrepareAsync(
            actor,
            orderId,
            request.WorkSessionId,
            request.UserId,
            cancellationToken);
        var draft = await ImportPreparedDraftAsync(
            actor, prepared, request, idempotencyKey, cancellationToken);
        return new RecoveredOrderSale(
            prepared.Order.OrderId,
            draft.DraftId,
            draft.Version,
            prepared.Order.OrderNumber,
            draft.PayableAmount);
    }

    internal async Task<OnlineSalesDraft> RecoverKnownAsync(
        OrderActor actor,
        OrderDetail order,
        RecoverOrderIntoSaleRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ValidateRequest(request);
        var prepared = await PrepareOrderAsync(
            actor,
            order,
            request.WorkSessionId,
            request.UserId,
            cancellationToken,
            replaceOtherClaim: true);
        return await ImportPreparedDraftAsync(
            actor, prepared, request, idempotencyKey, cancellationToken);
    }

    private async Task<OnlineSalesDraft> ImportPreparedDraftAsync(
        OrderActor actor,
        PreparedOrderRecovery prepared,
        RecoverOrderIntoSaleRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
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
                        line.LineTotal,
                        line.PriceSource,
                        line.DocumentUnitCost)).ToArray(),
                    request.ExpectedDraftVersion,
                    order.PartySiteId),
                idempotencyKey,
                cancellationToken);
            importCompleted = true;
            return draft;
        }
        catch when (!importCompleted && prepared.ClaimAcquiredByThisAttempt)
        {
            await orders.ReleaseClaimAsync(
                actor,
                order.OrderId,
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
        return await PrepareOrderAsync(
            actor, order, workSessionId, userId, cancellationToken);
    }

    private async Task<PreparedOrderRecovery> PrepareOrderAsync(
        OrderActor actor,
        OrderDetail order,
        Guid workSessionId,
        Guid userId,
        CancellationToken cancellationToken,
        bool replaceOtherClaim = false)
    {
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
            var claimRequest = new ClaimOrderRequest(workSessionId, userId);
            if (replaceOtherClaim)
                await orders.ClaimReplacingOtherAsync(
                    actor, order.OrderId, claimRequest, cancellationToken);
            else
                await orders.ClaimAsync(
                    actor, order.OrderId, claimRequest, cancellationToken);
            if (!replaceOtherClaim)
                await orders.ReleaseOtherClaimsAsync(
                    actor,
                    order.OrderId,
                    workSessionId,
                    userId,
                    cancellationToken);
            return new PreparedOrderRecovery(order, claimAcquiredByThisAttempt);
        }
        catch when (claimAcquiredByThisAttempt)
        {
            await orders.ReleaseClaimAsync(
                actor,
                order.OrderId,
                new ReleaseOrderClaimRequest(workSessionId, userId),
                cancellationToken);
            throw;
        }
    }

    private static void ValidateRequest(RecoverOrderIntoSaleRequest request)
    {
        if (request.DraftId == Guid.Empty || request.ExpectedDraftVersion < 1)
            throw new OrderValidationException(
                "La venta activa y su versión son obligatorias.");
    }
}
