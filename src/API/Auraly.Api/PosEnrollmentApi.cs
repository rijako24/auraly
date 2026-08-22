using System.Security.Claims;
using Auraly.Application.Authentication;
using Auraly.Application.Organization;
using Auraly.Contracts.Organization;

namespace Auraly.Api;

public static class PosEnrollmentApi
{
    public static IEndpointRouteBuilder MapPosEnrollmentApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/commerce/v1/pos/enrollments",
                async (
                    HttpContext context,
                    CreatePosEnrollmentRequest request,
                    PosEnrollmentService service,
                    CancellationToken ct) =>
                    await Handle(() => service.AuthorizeAsync(
                        context.User.ToPosEnrollmentUserIdentity(), request, ct)))
            .RequireAuthorization("pos.user");

        endpoints.MapPost(
                "/api/pos/v1/enrollments/redeem",
                async (
                    RedeemPosEnrollmentRequest request,
                    PosEnrollmentService service,
                    CancellationToken ct) =>
                    await Handle(() => service.RedeemAsync(request, ct)))
            .AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (PosEnrollmentForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: 403);
        }
        catch (PosEnrollmentValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: 400);
        }
        catch (PosEnrollmentConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: 409,
                title: "PosEnrollmentConflict");
        }
        catch (OfflineAuthenticationLeaseConfigurationException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "PosOfflineAccessUnavailable");
        }
    }
}

public static class PosEnrollmentClaimsExtensions
{
    public static PosEnrollmentUserIdentity ToPosEnrollmentUserIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            principal.PosUserDisplayName(),
            principal.FindAll("permission").Select(x => x.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirstValue(type), out var value)
            ? value
            : throw new PosEnrollmentForbiddenException(
                $"The authenticated identity lacks claim '{type}'.");
}
