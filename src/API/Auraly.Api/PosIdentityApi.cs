using System.Security.Claims;
using Auraly.Application.Authorization;

namespace Auraly.Api;

public static class PosIdentityApi
{
    public static IEndpointRouteBuilder MapPosIdentityApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/pos/v1/identity/snapshot",
                async (
                    HttpContext context,
                    PosOfflineIdentityService service,
                    CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await service.SnapshotAsync(
                            context.User.ToPosIdentityDeviceScope(), ct));
                    }
                    catch (PosIdentityForbiddenException exception)
                    {
                        return Results.Problem(
                            exception.Message,
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                })
            .RequireAuthorization("pos.identity.sync");

        return endpoints;
    }
}

public static class PosIdentityClaimsExtensions
{
    public static PosIdentityDeviceScope ToPosIdentityDeviceScope(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, PosAuthenticationDefaults.DeviceIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.BusinessIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.RegisterIdClaim),
            principal.FindAll(PosAuthenticationDefaults.PermissionClaim)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new PosIdentityForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
