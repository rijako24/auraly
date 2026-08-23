using Auraly.Application.Sales;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Orders;

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
        if (request.DraftId == Guid.Empty || request.ExpectedDraftVersion < 1)
            throw new OrderValidationException(
                "La venta activa y su versión son obligatorias.");

        var order = await orders.GetAsync(actor, orderId, cancellationToken);
        if (!order.CanInvoice)
            throw new OrderConflictException(
                "El pedido no está disponible para facturar.");
        if (order.Lines.Any(line => line.ProductId is null))
            throw new OrderConflictException(
                "El pedido tiene productos sin equivalencia en el catálogo de Auraly.");

        var targetWasAlreadyClaimedBySession =
            order.Claim?.IsOwnedByCurrentActor == true;
        await orders.ClaimAsync(
            actor,
            orderId,
            new ClaimOrderRequest(request.WorkSessionId, request.UserId),
            cancellationToken);
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
                        line.DiscountAmount)).ToArray(),
                    request.ExpectedDraftVersion),
                idempotencyKey,
                cancellationToken);
            importCompleted = true;
            await orders.ReleaseOtherClaimsAsync(
                actor,
                orderId,
                request.WorkSessionId,
                request.UserId,
                cancellationToken);
            return new RecoveredOrderSale(
                order.OrderId,
                draft.DraftId,
                draft.Version,
                order.OrderNumber,
                draft.PayableAmount);
        }
        catch when (!importCompleted && !targetWasAlreadyClaimedBySession)
        {
            await orders.ReleaseClaimAsync(
                actor,
                orderId,
                new ReleaseOrderClaimRequest(request.WorkSessionId, request.UserId),
                cancellationToken);
            throw;
        }
    }
}
