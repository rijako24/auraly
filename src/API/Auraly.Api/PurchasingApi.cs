using System.Security.Claims;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;

namespace Auraly.Api;

public static class PurchasingApi
{
    public static IEndpointRouteBuilder MapPurchasingApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/purchase-orders",
            (HttpContext context, string? search, string? status, int? page, int? pageSize,
                PurchaseOrderService service, CancellationToken cancellationToken) =>
                ExecuteAsync(() => service.ListAsync(context.User.ToPurchasingIdentity(), search,
                    status, page ?? 1, pageSize ?? 25, cancellationToken)))
            .RequireAuthorization("purchasing.user");
        endpoints.MapGet("/api/commerce/v1/purchase-orders/{purchaseOrderId:guid}",
            (HttpContext context, Guid purchaseOrderId, PurchaseOrderService service,
                CancellationToken cancellationToken) => ExecuteAsync(() => service.GetAsync(
                    context.User.ToPurchasingIdentity(), purchaseOrderId, cancellationToken)))
            .RequireAuthorization("purchasing.user");
        endpoints.MapGet("/api/commerce/v1/purchase-orders/{purchaseOrderId:guid}/receipt-source",
            (HttpContext context, Guid purchaseOrderId, PurchaseOrderService service,
                CancellationToken cancellationToken) => ExecuteAsync(() => service.GetReceiptSourceAsync(
                    context.User.ToPurchasingIdentity(), purchaseOrderId, cancellationToken)))
            .RequireAuthorization("purchasing.user");
        endpoints.MapPut("/api/commerce/v1/purchase-orders/{purchaseOrderId:guid}/draft",
            (HttpContext context, Guid purchaseOrderId, SavePurchaseOrderDraftRequest request,
                PurchaseOrderService service, CancellationToken cancellationToken) =>
                purchaseOrderId != request.PurchaseOrderId
                    ? Task.FromResult<IResult>(Results.BadRequest("The route and request identifiers differ."))
                    : ExecuteAsync(() => service.SaveDraftAsync(context.User.ToPurchasingIdentity(),
                        request, cancellationToken)))
            .RequireAuthorization("purchasing.user");
        endpoints.MapPost("/api/commerce/v1/purchase-orders/confirm",
            async (HttpContext context, ConfirmPurchaseOrderRequest request,
                PurchaseOrderService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await service.ConfirmAsync(context.User.ToPurchasingIdentity(),
                        context.Request.Headers["Idempotency-Key"].ToString(), request, cancellationToken);
                    return Results.Created($"/api/commerce/v1/purchase-orders/{result.PurchaseOrderId:D}", result);
                }
                catch (PurchasingForbiddenException exception) { return Results.Problem(exception.Message, statusCode: 403); }
                catch (PurchasingValidationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
                catch (PurchasingConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
            }).RequireAuthorization("purchasing.user");
        endpoints.MapPost("/api/commerce/v1/purchase-orders/{purchaseOrderId:guid}/close",
            (HttpContext context, Guid purchaseOrderId, ClosePurchaseOrderRequest request,
                PurchaseOrderService service, CancellationToken cancellationToken) => ExecuteAsync(async () =>
                {
                    await service.CloseAsync(context.User.ToPurchasingIdentity(), purchaseOrderId,
                        request, cancellationToken);
                    return new { Closed = true };
                }))
            .RequireAuthorization("purchasing.user");
        endpoints.MapPost(
                "/api/commerce/v1/goods-receipts/confirm",
                async (HttpContext context, ConfirmGoodsReceiptRequest request,
                    GoodsReceiptService service, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var key = context.Request.Headers["Idempotency-Key"].ToString();
                        var result = await service.ConfirmAsync(
                            context.User.ToPurchasingIdentity(), key, request, cancellationToken);
                        return Results.Accepted(
                            $"/api/commerce/v1/goods-receipts/{result.DocumentId:D}", result);
                    }
                    catch (PurchasingForbiddenException exception)
                    {
                        return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
                    }
                    catch (PurchasingValidationException exception)
                    {
                        return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
                    }
                    catch (PurchasingConflictException exception)
                    {
                        return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
                    }
                })
            .RequireAuthorization("purchasing.user");
        endpoints.MapGet(
                "/api/commerce/v1/goods-receipts/options",
                (HttpContext context, GoodsReceiptWorkspaceService service,
                    CancellationToken cancellationToken) =>
                    ExecuteAsync(() => service.GetOptionsAsync(
                        context.User.ToPurchasingIdentity(), cancellationToken)))
            .RequireAuthorization("purchasing.user");

        endpoints.MapGet(
                "/api/commerce/v1/goods-receipts/products",
                (HttpContext context, Guid supplierId, string? search, bool? includeUnassociated,
                    int? page, int? pageSize, GoodsReceiptWorkspaceService service,
                    CancellationToken cancellationToken) =>
                    ExecuteAsync(() => service.FindProductsAsync(
                        context.User.ToPurchasingIdentity(), supplierId, search,
                        includeUnassociated ?? false, page ?? 1, pageSize ?? 50,
                        cancellationToken)))
            .RequireAuthorization("purchasing.user");

        endpoints.MapPost(
                "/api/commerce/v1/goods-receipts/supplier-products",
                (HttpContext context, AssociateGoodsReceiptProductRequest request,
                    GoodsReceiptWorkspaceService service, CancellationToken cancellationToken) =>
                    ExecuteAsync(() => service.AssociateProductAsync(
                        context.User.ToPurchasingIdentity(), request, cancellationToken)))
            .RequireAuthorization("purchasing.user");

        endpoints.MapGet(
                "/api/commerce/v1/goods-receipts",
                (HttpContext context, string? search, string? status, int? page, int? pageSize,
                    GoodsReceiptWorkspaceService service, CancellationToken cancellationToken) =>
                    ExecuteAsync(() => service.ListAsync(
                        context.User.ToPurchasingIdentity(), search, status,
                        page ?? 1, pageSize ?? 25, cancellationToken)))
            .RequireAuthorization("purchasing.user");

        endpoints.MapGet(
                "/api/commerce/v1/goods-receipts/{documentId:guid}",
                async (HttpContext context, Guid documentId, GoodsReceiptWorkspaceService service,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.GetDetailAsync(
                        context.User.ToPurchasingIdentity(), documentId, cancellationToken);
                    return result is null ? Results.NotFound() : Results.Ok(result);
                })
            .RequireAuthorization("purchasing.user");

        endpoints.MapGet(
                "/api/commerce/v1/goods-receipts/drafts/{draftId:guid}",
                async (HttpContext context, Guid draftId, GoodsReceiptWorkspaceService service,
                    CancellationToken cancellationToken) =>
                {
                    var result = await ExecuteAsync(() => service.GetDraftAsync(
                        context.User.ToPurchasingIdentity(), draftId, cancellationToken));
                    return result;
                })
            .RequireAuthorization("purchasing.user");

        endpoints.MapPut(
                "/api/commerce/v1/goods-receipts/drafts/{draftId:guid}",
                (HttpContext context, Guid draftId, SaveGoodsReceiptDraftRequest request,
                    GoodsReceiptWorkspaceService service, CancellationToken cancellationToken) =>
                    draftId != request.DraftId
                        ? Task.FromResult<IResult>(Results.Problem(
                            "The route DraftId does not match the request.",
                            statusCode: StatusCodes.Status400BadRequest))
                        : ExecuteAsync(() => service.SaveDraftAsync(
                            context.User.ToPurchasingIdentity(), request, cancellationToken)))
            .RequireAuthorization("purchasing.user");

        endpoints.MapDelete(
                "/api/commerce/v1/goods-receipts/drafts/{draftId:guid}",
                (HttpContext context, Guid draftId, string concurrencyToken,
                    GoodsReceiptWorkspaceService service, CancellationToken cancellationToken) =>
                    ExecuteAsync(async () =>
                    {
                        await service.DeleteDraftAsync(
                            context.User.ToPurchasingIdentity(), draftId,
                            concurrencyToken, cancellationToken);
                        return new { Deleted = true };
                    }))
            .RequireAuthorization("purchasing.user");

        endpoints.MapGet(
                "/api/commerce/v1/purchase-returns/receipts",
                (HttpContext context, string? search, DateOnly? from, DateOnly? to,
                    bool? withAvailableQuantity, int? page, int? pageSize,
                    PurchaseReturnService service, CancellationToken cancellationToken) =>
                    ExecuteAsync(() => service.ListReturnableReceiptsAsync(
                        context.User.ToPurchasingIdentity(), search, from, to,
                        withAvailableQuantity, page ?? 1, pageSize ?? 25,
                        cancellationToken)))
            .RequireAuthorization("purchasing.user");

        endpoints.MapGet(
                "/api/commerce/v1/purchase-returns/receipts/{goodsReceiptId:guid}",
                (HttpContext context, Guid goodsReceiptId, PurchaseReturnService service,
                    CancellationToken cancellationToken) =>
                    ExecuteAsync(() => service.GetReturnableReceiptAsync(
                        context.User.ToPurchasingIdentity(), goodsReceiptId,
                        cancellationToken)))
            .RequireAuthorization("purchasing.user");

        endpoints.MapPost(
                "/api/commerce/v1/purchase-returns/confirm",
                async (HttpContext context, ConfirmPurchaseReturnRequest request,
                    PurchaseReturnService service, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var key = context.Request.Headers["Idempotency-Key"].ToString();
                        var result = await service.ConfirmAsync(
                            context.User.ToPurchasingIdentity(), key, request,
                            cancellationToken);
                        return Results.Accepted(
                            $"/api/commerce/v1/purchase-returns/{result.ReturnId:D}", result);
                    }
                    catch (PurchasingForbiddenException exception)
                    {
                        return Results.Problem(exception.Message,
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                    catch (PurchasingValidationException exception)
                    {
                        return Results.Problem(exception.Message,
                            statusCode: StatusCodes.Status400BadRequest);
                    }
                    catch (PurchasingConflictException exception)
                    {
                        return Results.Problem(exception.Message,
                            statusCode: StatusCodes.Status409Conflict);
                    }
                })
            .RequireAuthorization("purchasing.user");
        return endpoints;
    }
    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            var result = await action();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (PurchasingForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PurchasingValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PurchasingConflictException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

}

public static class PurchasingClaimsPrincipalExtensions
{
    public static PurchasingUserIdentity ToPurchasingIdentity(this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            principal.FindAll("permission").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new PurchasingForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
