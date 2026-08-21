using Auraly.Application.Orders;
using Auraly.Contracts.Orders;

namespace Auraly.Api;

public static class PosOrdersApi
{
    public static IEndpointRouteBuilder MapPosOrdersApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pos/v1/orders")
            .RequireAuthorization("pos.orders");

        group.MapGet("/", async (
            HttpContext context,
            Guid userId,
            Guid businessId,
            Guid warehouseId,
            Guid workSessionId,
            int? page,
            int? pageSize,
            string? orderNumber,
            string? customer,
            string? product,
            string? status,
            DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo,
            OrderService orders,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = await actors.ResolveAsync(
                    context.User.ToPosDeviceIdentity(), new PosOrderExecutionContext(userId, businessId, warehouseId, workSessionId), ct);
                return await orders.PageAsync(actor, new OrderPageRequest(
                    page ?? 1,
                    pageSize ?? 50,
                    orderNumber,
                    customer,
                    product,
                    status,
                    CreatedFrom: createdFrom,
                    CreatedTo: createdTo), ct);
            }));

        group.MapGet("/{orderId:guid}", async (
            HttpContext context,
            Guid orderId,
            Guid userId,
            Guid businessId,
            Guid warehouseId,
            Guid workSessionId,
            OrderService orders,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = await actors.ResolveAsync(
                    context.User.ToPosDeviceIdentity(), new PosOrderExecutionContext(userId, businessId, warehouseId, workSessionId), ct);
                return await orders.GetAsync(actor, orderId, ct);
            }));

        group.MapPost("/{orderId:guid}/claim", async (
            HttpContext context,
            Guid orderId,
            PosOrderUserRequest request,
            OrderService orders,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var device = context.User.ToPosDeviceIdentity();
                var actor = await actors.ResolveAsync(device, request.ToExecutionContext(), ct);
                return await orders.ClaimAsync(actor, orderId, new ClaimOrderRequest(
                    request.WorkSessionId, request.UserId, request.LeaseMinutes), ct);
            }));

        group.MapPost("/{orderId:guid}/claim/release", async (
            HttpContext context,
            Guid orderId,
            PosOrderUserRequest request,
            OrderService orders,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var device = context.User.ToPosDeviceIdentity();
                var actor = await actors.ResolveAsync(device, request.ToExecutionContext(), ct);
                await orders.ReleaseClaimAsync(actor, orderId,
                    new ReleaseOrderClaimRequest(request.WorkSessionId, request.UserId), ct);
                return new { released = true };
            }));

        group.MapPost("/invoice", async (
            HttpContext context,
            PosInvoiceOrdersRequest request,
            OrderBatchService batches,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var device = context.User.ToPosDeviceIdentity();
                var actor = await actors.ResolveAsync(device, request.ToExecutionContext(), ct);
                return await batches.InvoiceAsync(actor, new InvoiceOrdersRequest(
                    request.WorkSessionId,
                    request.WarehouseId,
                    request.UserId,
                    request.OrderIds.ToArray(),
                    request.PaymentMethodCode,
                    request.PaymentReference,
                    request.DocumentType),
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    ct);
            }));

        return endpoints;
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (OrderForbiddenException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OrderNotFoundException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (OrderValidationException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (OrderConflictException error)
        {
            return Results.Problem(
                error.Message, statusCode: StatusCodes.Status409Conflict, title: "OrderConflict");
        }
    }
}

public sealed record PosOrderUserRequest(
    Guid UserId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId,
    int LeaseMinutes = 10)
{
    public PosOrderExecutionContext ToExecutionContext() =>
        new(UserId, BusinessId, WarehouseId, WorkSessionId);
}

public sealed record PosInvoiceOrdersRequest(
    Guid UserId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId,
    IReadOnlyCollection<Guid> OrderIds,
    string PaymentMethodCode,
    string? PaymentReference,
    string DocumentType = "SalesInvoice")
{
    public PosOrderExecutionContext ToExecutionContext() =>
        new(UserId, BusinessId, WarehouseId, WorkSessionId);
}
