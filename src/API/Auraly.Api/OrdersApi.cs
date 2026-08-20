using System.Security.Claims;
using Auraly.Application.Orders;
using Auraly.Application.Sales;
using Auraly.Contracts.Orders;

namespace Auraly.Api;

public static class OrdersApi
{
    public static IEndpointRouteBuilder MapOrdersApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/orders")
            .RequireAuthorization("orders.user");

        group.MapGet("/", async (
            HttpContext context,
            OrderService service,
            int? page,
            int? pageSize,
            string? orderNumber,
            string? customer,
            string? product,
            string? status,
            int? source,
            DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo,
            bool? hasPendingBalance,
            bool? includeClaimedByOthers,
            Guid? warehouseId,
            Guid? routeId,
            bool? onlyMine,
            CancellationToken ct) =>
            await Handle(() => service.PageAsync(
                context.User.ToOrderUserActor(),
                new OrderPageRequest(
                    page ?? 1,
                    pageSize ?? 50,
                    orderNumber,
                    customer,
                    product,
                    status,
                    source,
                    createdFrom,
                    createdTo,
                    hasPendingBalance,
                    includeClaimedByOthers ?? true,
                    WarehouseId: warehouseId,
                    RouteId: routeId,
                    OnlyCreatedByActor: onlyMine ?? false),
                ct)));

        group.MapGet("/{orderId:guid}", async (
            HttpContext context,
            Guid orderId,
            OrderService service,
            CancellationToken ct) =>
            await Handle(() => service.GetAsync(
                context.User.ToOrderUserActor(), orderId, ct)));

        group.MapPost("/{orderId:guid}/claim", async (
            HttpContext context,
            Guid orderId,
            ClaimOrderRequest request,
            OrderService service,
            CancellationToken ct) =>
            await Handle(() => service.ClaimAsync(
                context.User.ToOrderUserActor(request.WorkSessionId), orderId, request, ct)));

        group.MapPost("/{orderId:guid}/claim/release", async (
            HttpContext context,
            Guid orderId,
            ReleaseOrderClaimRequest request,
            OrderService service,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                await service.ReleaseClaimAsync(
                    context.User.ToOrderUserActor(request.WorkSessionId), orderId, request, ct);
                return new { released = true };
            }));

        group.MapPost("/{orderId:guid}/recover", async (
            HttpContext context,
            Guid orderId,
            RecoverOrderIntoSaleRequest request,
            OrderRecoveryService service,
            CancellationToken ct) =>
            await Handle(() => service.RecoverAsync(
                context.User.ToOrderUserActor(request.WorkSessionId),
                orderId,
                request,
                context.Request.Headers["Idempotency-Key"].ToString(),
                ct)));

        group.MapPost("/invoice", async (
            HttpContext context,
            InvoiceOrdersRequest request,
            OrderBatchService service,
            SellerOrderInvoiceInventoryService sellerInventory,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = context.User.ToOrderUserActor(request.WorkSessionId);
                await sellerInventory.PrepareAsync(actor, request, ct);
                return await service.InvoiceAsync(
                    actor,
                    request,
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    ct);
            }));


        return endpoints;
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (OrderForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OrderNotFoundException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (OrderValidationException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (OrderConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "OrderConflict");
        }
        catch (OnlineSalesDraftForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OnlineSalesDraftValidationException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

public static class OrdersClaimsPrincipalExtensions
{
    public static OrderActor ToOrderUserActor(
        this ClaimsPrincipal principal,
        Guid? workSessionId = null) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            workSessionId,
            null,
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(
        ClaimsPrincipal principal,
        string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new OrderForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
