using System.Security.Claims;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;

namespace Auraly.Api;

public static class PurchasingApi
{
    public static IEndpointRouteBuilder MapPurchasingApi(this IEndpointRouteBuilder endpoints)
    {
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
                (HttpContext context, string? search, int? page, int? pageSize,
                    PurchaseReturnService service, CancellationToken cancellationToken) =>
                    ExecuteAsync(() => service.ListReturnableReceiptsAsync(
                        context.User.ToPurchasingIdentity(), search, page ?? 1,
                        pageSize ?? 25, cancellationToken)))
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
