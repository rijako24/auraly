using System.Security.Claims;
using Auraly.Application.Returns;
using Auraly.Contracts.Returns;

namespace Auraly.Api;

public static class ReturnsApi
{
    public static IEndpointRouteBuilder MapReturnsApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/commerce/v1/sales-returns/confirm", async (
                HttpContext context,
                ConfirmSalesReturnRequest request,
                SalesReturnService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var key = context.Request.Headers["Idempotency-Key"].ToString();
                    var result = await service.ConfirmAsync(
                        context.User.ToSalesReturnIdentity(), key, request, cancellationToken);
                    return Results.Accepted(
                        $"/api/commerce/v1/sales-returns/{result.ReturnId:D}", result);
                }
                catch (SalesReturnForbiddenException exception)
                {
                    return Results.Problem(exception.Message,
                        statusCode: StatusCodes.Status403Forbidden);
                }
                catch (SalesReturnValidationException exception)
                {
                    return Results.Problem(exception.Message,
                        statusCode: StatusCodes.Status400BadRequest);
                }
                catch (SalesReturnConflictException exception)
                {
                    return Results.Problem(exception.Message,
                        statusCode: StatusCodes.Status409Conflict);
                }
            })
            .RequireAuthorization("returns.user");
        return endpoints;
    }

    private static SalesReturnUserIdentity ToSalesReturnIdentity(
        this ClaimsPrincipal principal) => new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            principal.FindAll("permission")
                .Concat(principal.FindAll(PosAuthenticationDefaults.PermissionClaim))
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new SalesReturnForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
