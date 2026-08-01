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
        return endpoints;
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
