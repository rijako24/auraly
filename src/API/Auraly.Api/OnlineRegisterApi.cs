using System.Security.Claims;
using Auraly.Application.Organization;
using Auraly.Contracts.Organization;

namespace Auraly.Api;

public static class OnlineRegisterApi
{
    public static IEndpointRouteBuilder MapOnlineRegisterApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/register-context")
            .RequireAuthorization("pos.user");

        group.MapGet("/options", async (
            HttpContext context, OnlineRegisterService service, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ListAsync(
                context.User.ToOnlineRegisterUserIdentity(), ct))));

        group.MapPost("/select", async (
            HttpContext context, OnlineRegisterService service,
            OnlineRegisterSelection selection, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SelectAsync(
                context.User.ToOnlineRegisterUserIdentity(), selection, ct))));

        return endpoints;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (OnlineRegisterForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OnlineRegisterValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

public static class OnlineRegisterClaimsPrincipalExtensions
{
    public static OnlineRegisterUserIdentity ToOnlineRegisterUserIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            principal.FindAll("permission").Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new OnlineRegisterForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
