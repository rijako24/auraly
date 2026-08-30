using System.Security.Claims;
using Auraly.BuildingBlocks.Application.Synchronization;

namespace Auraly.Api;

public static class PosSynchronizationApi
{
    public static IEndpointRouteBuilder MapPosSynchronizationApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/pos/v1/synchronization/negotiate",
                (HttpContext context,
                    IPosSynchronizationPushGateway gateway,
                    Guid businessId,
                    CancellationToken cancellationToken) =>
                {
                    var tenantId = RequiredGuid(
                        context.User,
                        PosAuthenticationDefaults.TenantIdClaim);

                    var deviceId = RequiredGuid(
                        context.User,
                        PosAuthenticationDefaults.DeviceIdClaim);
                    var uri = gateway.CreateClientAccessUri(
                        tenantId,
                        businessId,
                        deviceId,
                        cancellationToken);
                    return Results.Ok(new PosSynchronizationNegotiationResponse(
                        uri,
                        DateTimeOffset.UtcNow.AddMinutes(15),
                        [
                            PosSynchronizationGroups.Business(tenantId, businessId),
                            PosSynchronizationGroups.Device(tenantId, deviceId)
                        ]));
                })
            .RequireAuthorization("pos.synchronization");
        return endpoints;
    }

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) &&
        value != Guid.Empty
            ? value
            : throw new UnauthorizedAccessException(
                $"The authenticated device lacks claim '{claimType}'.");
}
