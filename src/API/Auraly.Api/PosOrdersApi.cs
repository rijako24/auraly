using Auraly.Application.Orders;
using Auraly.Application.Sales;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;

namespace Auraly.Api;

public static class PosOrdersApi
{
    public static IEndpointRouteBuilder MapPosOrdersApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pos/v1/orders")
            .RequireAuthorization("pos.enrolled");

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

        group.MapPost("/save", async (
            HttpContext context,
            PosSaveOrderRequest request,
            SellerOrderWriter writer,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var orderActor = await actors.ResolveAsync(
                    context.User.ToPosDeviceIdentity(),
                    new PosOrderExecutionContext(
                        request.UserId,
                        request.BusinessId,
                        request.WarehouseId,
                        request.WorkSessionId),
                    ct);
                var actor = new SellerOrderActor(
                    orderActor.UserId,
                    orderActor.TenantId,
                    orderActor.BusinessId,
                    orderActor.Permissions);
                var lines = request.Lines.Select(line =>
                    new SellerOrdersApi.SellerOrderLineInput(
                        line.ProductId,
                        line.Quantity,
                        line.UnitPrice,
                        line.DiscountAmount,
                        line.PriceSource,
                        line.DocumentUnitCost)).ToArray();
                SellerOrdersApi.SellerOrderResult result;
                if (request.OrderId is Guid orderId)
                {
                    result = await writer.UpdateReviewAsync(
                        actor,
                        orderId,
                        new SellerOrdersApi.UpdateSellerOrderRequest(
                            request.CustomerId,
                            request.Notes,
                            request.IdempotencyKey,
                            lines,
                            request.WorkSessionId),
                        ct);
                }
                else
                {
                    result = await writer.CreateAsync(
                        actor,
                        new SellerOrdersApi.CreateSellerOrderRequest(
                            request.BusinessId,
                            request.WarehouseId,
                            request.CustomerId,
                            null,
                            null,
                            null,
                            false,
                            request.Notes,
                            request.IdempotencyKey,
                            lines),
                        ct);
                }
                return new PosSaveOrderResponse(
                    result.OrderId,
                    result.OrderNumber,
                    result.Status,
                    result.Total,
                    result.RequiresReview,
                    result.Warnings);
            }));

        group.MapPost("/print-batch", async (
            HttpContext context,
            PosPrintOrdersRequest request,
            OrderService orders,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = await actors.ResolveAsync(
                    context.User.ToPosDeviceIdentity(), request.ToExecutionContext(), ct);
                return await orders.GetPrintBatchAsync(
                    actor, new OrderPrintBatchRequest(request.OrderIds), ct);
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

        group.MapPost("/{orderId:guid}/prepare-recovery", async (
            HttpContext context,
            Guid orderId,
            PosOrderUserRequest request,
            OrderRecoveryService recovery,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = await actors.ResolveAsync(
                    context.User.ToPosDeviceIdentity(), request.ToExecutionContext(), ct);
                return (await recovery.PrepareAsync(
                    actor,
                    orderId,
                    request.WorkSessionId,
                    request.UserId,
                    ct)).Order;
            }));

        group.MapPost("/{orderId:guid}/cancel", async (
            HttpContext context,
            Guid orderId,
            PosCancelOrderRequest request,
            OrderCancellationService service,
            PosApprovalService approvals,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var device = context.User.ToPosDeviceIdentity();
                var actor = await actors.ResolveAsync(
                    device, request.ToExecutionContext(), ct);
                var cancellation = new CancelOrderRequest(
                    request.Reason, request.WorkSessionId);
                var key = context.Request.Headers["Idempotency-Key"].ToString();
                if (actor.Permissions.Contains(OrderPermissionCodes.Cancel))
                    return await service.CancelAsync(
                        actor, orderId, cancellation, key, ct);

                var authorization = request.RestartAuthorization
                    ?? throw new OrderForbiddenException(
                        $"Permission '{OrderPermissionCodes.Cancel}' is required.");
                if (!string.Equals(
                        authorization.PermissionResource,
                        CommercePermissionCodes.SalesRestartDraft,
                        StringComparison.Ordinal))
                    throw new OrderForbiddenException(
                        "La autorización no corresponde a reiniciar la venta.");
                if (!actor.Permissions.Contains(authorization.PermissionResource))
                {
                    if (authorization.ApprovalRequestId is Guid approvalRequestId)
                    {
                        await approvals.ReserveForDeviceAsync(
                            device.TenantId,
                            device.DeviceId,
                            approvalRequestId,
                            new ReservePosApprovalForDeviceRequest(
                                request.BusinessId,
                                request.UserId,
                                request.WorkSessionId,
                                authorization.DraftId,
                                null,
                                authorization.PermissionResource,
                                authorization.OperationId),
                            ct);
                    }
                    else
                    {
                        await approvals.ValidateLocalDeviceAuthorizerAsync(
                            device.TenantId,
                            request.BusinessId,
                            request.UserId,
                            authorization.AuthorizedByUserId,
                            authorization.PermissionResource,
                            ct);
                    }
                }
                return await service.CancelAuthorizedAsync(
                    actor, orderId, cancellation, key, ct);
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
                    request.DocumentType,
                    request.BankAccountId,
                    request.PaymentNotes),
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    ct);
            }));

        group.MapGet("/documents/{documentId:guid}/receipt", async (
            HttpContext context,
            Guid documentId,
            Guid userId,
            Guid businessId,
            Guid warehouseId,
            Guid workSessionId,
            OnlineSalesHistoryService history,
            IPosOrderActorResolver actors,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = await actors.ResolveAsync(
                    context.User.ToPosDeviceIdentity(),
                    new PosOrderExecutionContext(
                        userId, businessId, warehouseId, workSessionId), ct);
                return await history.GetReceiptAsync(
                    new OnlineSalesUserIdentity(
                        actor.UserId, actor.TenantId, actor.Permissions),
                    new OnlineSalesDraftContext(
                        businessId, warehouseId, workSessionId),
                    documentId,
                    ct) ?? throw new OrderNotFoundException(
                        "No se encontro el documento facturado.");
            }));

        return endpoints;
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (PosApprovalException error)
        {
            var statusCode = error.Code is "Forbidden" or "SelfApprovalForbidden"
                ? StatusCodes.Status403Forbidden
                : error.Code is "InvalidApproval" or "AlreadyDecidedOrExpired"
                    ? StatusCodes.Status409Conflict
                    : error.Code == "ApprovalRequired"
                        ? StatusCodes.Status428PreconditionRequired
                        : StatusCodes.Status400BadRequest;
            return Results.Problem(error.Message, statusCode: statusCode, title: error.Code);
        }
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
        catch (OnlineSalesDraftForbiddenException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OnlineSalesDraftValidationException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (OnlineSalesDraftConcurrencyException error)
        {
            return Results.Problem(
                error.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "OrderInventoryConflict");
        }
        catch (SellerOrderForbiddenException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (SellerOrderValidationException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (SellerOrderConflictException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict);
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
    string DocumentType = "SalesInvoice",
    Guid? BankAccountId = null,
    string? PaymentNotes = null)
{
    public PosOrderExecutionContext ToExecutionContext() =>
        new(UserId, BusinessId, WarehouseId, WorkSessionId);
}

public sealed record PosCancelOrderRequest(
    Guid UserId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId,
    string Reason,
    PosRestartOrderAuthorization? RestartAuthorization = null)
{
    public PosOrderExecutionContext ToExecutionContext() =>
        new(UserId, BusinessId, WarehouseId, WorkSessionId);
}

public sealed record PosRestartOrderAuthorization(
    Guid DraftId,
    string PermissionResource,
    Guid AuthorizedByUserId,
    Guid? ApprovalRequestId,
    Guid OperationId);

public sealed record PosPrintOrdersRequest(
    Guid UserId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId,
    IReadOnlyCollection<Guid> OrderIds)
{
    public PosOrderExecutionContext ToExecutionContext() =>
        new(UserId, BusinessId, WarehouseId, WorkSessionId);
}
