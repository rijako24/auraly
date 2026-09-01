using System.Security.Claims;
using Auraly.Application.Authentication;
using Auraly.Contracts.Authentication;

namespace Auraly.Api;

public static class OfflineAuthenticationLeaseApi
{
    public static IEndpointRouteBuilder MapOfflineAuthenticationLeaseApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/pos/v1/authentication/offline-leases")
            .RequireAuthorization("pos.enrolled");

        group.MapPost("/", async (
            HttpContext context,
            OfflineAuthenticationLeaseAcquireRequest request,
            OfflineAuthenticationLeaseService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.AcquireAsync(
                Device(context.User), request, cancellationToken))));

        group.MapPost("/{leaseId:guid}/release", async (
            HttpContext context,
            Guid leaseId,
            OfflineAuthenticationLeaseService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                await service.ReleaseAsync(
                    Device(context.User), leaseId, cancellationToken);
                return Results.NoContent();
            }));

        group.MapGet("/{leaseId:guid}/active", async (
            HttpContext context,
            Guid leaseId,
            Guid userId,
            OfflineAuthenticationLeaseService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(new
            {
                active = await service.IsActiveAsync(
                    Device(context.User), leaseId, userId, cancellationToken)
            })));

        return endpoints;
    }

    private static OfflineAuthenticationLeaseDevice Device(ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.DeviceIdClaim));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new AuthenticationValidationException(
                $"The enrolled device identity lacks claim '{claimType}'.");

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AuthenticationValidationException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "OfflineAuthenticationLeaseValidationFailed");
        }
        catch (AuthenticationDeniedException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status401Unauthorized,
                title: "OfflineAuthenticationDenied");
        }
        catch (OfflineAuthenticationLeaseConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "OfflineAuthenticationLeaseConflict");
        }
        catch (OfflineAuthenticationLeaseConfigurationException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "OfflineAuthenticationLeaseUnavailable");
        }
    }
}
