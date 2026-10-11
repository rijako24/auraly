using Auraly.Infrastructure.Persistence;

namespace Auraly.Api;

public sealed record RestaurantPrintAcknowledgement(
    Guid LeaseToken, string State, string? Error);

public static class RestaurantCommandPrintApi
{
    public static IEndpointRouteBuilder MapRestaurantCommandPrintApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pos/v1/restaurant/print-jobs")
            .RequireAuthorization("pos.enrolled");
        group.MapPost("/claim", async (HttpContext context,
            SqlRestaurantCommandPrintStore store, CancellationToken ct) =>
        {
            var identity = context.User.ToPosDeviceIdentity();
            return Results.Ok(await store.ClaimAsync(identity.TenantId,
                identity.DeviceId, ct));
        });
        group.MapPost("/{cardId:guid}/ack", async (HttpContext context,
            Guid cardId, RestaurantPrintAcknowledgement request,
            SqlRestaurantCommandPrintStore store, CancellationToken ct) =>
        {
            if (request.LeaseToken == Guid.Empty ||
                request.State is not ("Submitted" or "Failed" or "Uncertain"))
                return Results.BadRequest();
            var identity = context.User.ToPosDeviceIdentity();
            return await store.AcknowledgeAsync(identity.TenantId,
                identity.DeviceId, cardId, request.LeaseToken,
                request.State, request.Error, ct)
                ? Results.NoContent() : Results.Conflict();
        });
        return endpoints;
    }
}
