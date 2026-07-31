using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Orders;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosOrderRecoveryService(
    PosOrderServerClient server,
    PosCatalogStore catalog,
    PosDraftStore drafts,
    PosEdgeRuntimeContext runtime)
{
    public async Task<PosDraft> RecoverAsync(
        Guid userId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await server.ClaimAsync(userId, orderId, cancellationToken);
        try
        {
            var order = await server.GetAsync(userId, orderId, cancellationToken);
            var lines = new List<PosDraftLineInput>(order.Lines.Count);
            foreach (var orderLine in order.Lines)
            {
                if (orderLine.ProductId is null)
                    throw new InvalidOperationException(
                        $"El producto '{orderLine.ProductName}' del pedido no está vinculado al catálogo.");
                var product = await catalog.GetByProductIdAsync(
                    orderLine.ProductId.Value, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"El producto '{orderLine.ProductName}' no está disponible en el catálogo local.");
                lines.Add(new PosDraftLineInput(
                    new ProductId(product.ProductId),
                    product.ProductCode,
                    product.Name,
                    product.BaseUnitCode,
                    product.TaxCode,
                    product.TaxRate,
                    orderLine.Quantity,
                    orderLine.UnitPrice,
                    orderLine.UnitPrice,
                    order.Currency,
                    "Order",
                    Discount: orderLine.DiscountAmount,
                    Note: $"Pedido {order.OrderNumber}"));
            }

            return await drafts.ImportOrderAsync(
                runtime.ScopeFor(userId),
                order.OrderId,
                order.CustomerId,
                lines,
                cancellationToken);
        }
        catch
        {
            try
            {
                await server.ReleaseAsync(userId, orderId, CancellationToken.None);
            }
            catch (Exception releaseError)
                when (releaseError is HttpRequestException or PosOrderServerException)
            {
                // The server lease expires durably; never hide the original recovery error.
            }
            throw;
        }
    }
}

public static class PosOrderEndpoints
{
    public static RouteGroupBuilder MapPosOrders(this RouteGroupBuilder edge)
    {
        edge.MapGet("/orders", async (
            HttpRequest request,
            PosOrderServerClient server,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var query = request.QueryString.HasValue
                ? request.QueryString.Value![1..]
                : "page=1&pageSize=50";
            return Results.Ok(await server.PageAsync(
                sessions.Required().UserId, query, ct));
        });

        edge.MapGet("/orders/{orderId:guid}", async (
            Guid orderId,
            PosOrderServerClient server,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await server.GetAsync(
                sessions.Required().UserId, orderId, ct)));

        edge.MapPost("/orders/{orderId:guid}/recover", async (
            Guid orderId,
            PosOrderRecoveryService recovery,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await recovery.RecoverAsync(
                sessions.Required().UserId, orderId, ct)));

        edge.MapPost("/orders/invoice", async (
            InvoicePosOrdersRequest request,
            PosOrderServerClient server,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await server.InvoiceAsync(
                sessions.Required().UserId,
                request.OrderIds,
                request.PaymentMethodCode,
                request.IdempotencyKey,
                ct)));

        return edge;
    }
}

public sealed record InvoicePosOrdersRequest(
    IReadOnlyCollection<Guid> OrderIds,
    string PaymentMethodCode,
    string? PaymentReference,
    string IdempotencyKey);
