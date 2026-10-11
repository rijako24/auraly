using Auraly.Application.Sales;
using Auraly.Contracts.Sales;

namespace Auraly.Api;

public static class RestaurantMasterApi
{
    public static IEndpointRouteBuilder MapRestaurantMasterApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/restaurant")
            .RequireAuthorization("pos.user");
        group.MapGet("/rooms", async (
            HttpContext context, RestaurantMasterService service,
            int? page, int? pageSize, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.RoomsAsync(
                Actor(context), page ?? 1, pageSize ?? 50, ct))));
        group.MapPost("/rooms", async (
            HttpContext context, RestaurantMasterService service,
            CreateRestaurantRoomRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Created(
                $"/api/commerce/v1/restaurant/rooms/{request.RoomId}",
                await service.CreateRoomAsync(Actor(context), request, ct))));
        group.MapGet("/rooms/{roomId:guid}/floorplan", async (
            HttpContext context, RestaurantMasterService service,
            Guid roomId, bool? draft, CancellationToken ct) =>
            await Handle(async () =>
            {
                var plan = await service.FloorPlanAsync(Actor(context), roomId,
                    draft == true, ct);
                return plan is null ? Results.NotFound() : Results.Ok(plan);
            }));
        group.MapPut("/rooms/{roomId:guid}/floorplan", async (
            HttpContext context, RestaurantMasterService service,
            Guid roomId, SaveRestaurantFloorPlanRequest request,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SaveFloorPlanAsync(
                Actor(context), roomId, request, ct))));
        group.MapPost("/rooms/{roomId:guid}/floorplan/publish", async (
            HttpContext context, RestaurantMasterService service,
            Guid roomId, PublishRestaurantFloorPlanRequest request,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PublishFloorPlanAsync(
                Actor(context), roomId, request, ct))));
        group.MapPut("/rooms/{roomId:guid}", async (
            HttpContext context, RestaurantMasterService service,
            Guid roomId, UpdateRestaurantRoomRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.UpdateRoomAsync(
                Actor(context), roomId, request, ct))));
        group.MapGet("/tables", async (
            HttpContext context, RestaurantMasterService service,
            int? page, int? pageSize, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.TablesAsync(
                Actor(context), page ?? 1, pageSize ?? 50, ct))));
        group.MapPost("/tables", async (
            HttpContext context, RestaurantMasterService service,
            CreateRestaurantTableRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Created(
                $"/api/commerce/v1/restaurant/tables/{request.TableId}",
                await service.CreateTableAsync(Actor(context), request, ct))));
        group.MapPut("/tables/{tableId:guid}", async (
            HttpContext context, RestaurantMasterService service,
            Guid tableId, UpdateRestaurantTableRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.UpdateTableAsync(
                Actor(context), tableId, request, ct))));
        group.MapGet("/stations", async (
            HttpContext context, RestaurantMasterService service,
            int? page, int? pageSize, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.StationsAsync(
                Actor(context), page ?? 1, pageSize ?? 50, ct))));
        group.MapGet("/print-devices", async (
            HttpContext context, RestaurantMasterService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PrintDevicesAsync(
                Actor(context), ct))));
        group.MapGet("/stations/{stationId:guid}/command-configuration", async (
            HttpContext context, RestaurantMasterService service,
            Guid stationId, CancellationToken ct) =>
            await Handle(async () =>
            {
                var configuration = await service.CommandConfigurationAsync(
                    Actor(context), stationId, ct);
                return configuration is null ? Results.NotFound() :
                    Results.Ok(configuration);
            }));
        group.MapPut("/stations/{stationId:guid}/command-configuration", async (
            HttpContext context, RestaurantMasterService service,
            Guid stationId, UpdateRestaurantCommandConfigurationRequest request,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await
                service.UpdateCommandConfigurationAsync(
                    Actor(context), stationId, request, ct))));
        group.MapPost("/stations", async (
            HttpContext context, RestaurantMasterService service,
            CreateRestaurantStationRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Created(
                $"/api/commerce/v1/restaurant/stations/{request.StationId}",
                await service.CreateStationAsync(Actor(context), request, ct))));
        group.MapPut("/stations/{stationId:guid}", async (
            HttpContext context, RestaurantMasterService service,
            Guid stationId, UpdateRestaurantStationRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.UpdateStationAsync(
                Actor(context), stationId, request, ct))));
        group.MapGet("/product-routes", async (
            HttpContext context, RestaurantMasterService service,
            int? page, int? pageSize, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ProductRoutesAsync(
                Actor(context), page ?? 1, pageSize ?? 50, ct))));
        group.MapGet("/product-routes/{productId:guid}", async (
            HttpContext context, RestaurantMasterService service,
            Guid productId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ProductRouteAsync(
                Actor(context), productId, ct))));
        group.MapPut("/product-routes/{productId:guid}", async (
            HttpContext context, RestaurantMasterService service,
            Guid productId, SetRestaurantProductRouteRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SetProductRouteAsync(
                Actor(context), productId, request, ct))));
        group.MapDelete("/product-routes/{productId:guid}", async (
            HttpContext context, RestaurantMasterService service,
            Guid productId, string rowVersion, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.DeleteProductRouteAsync(
                Actor(context), productId, rowVersion, ct))));
        return endpoints;
    }

    private static RestaurantMasterActor Actor(HttpContext context)
    {
        var identity = context.User.ToPartyUserIdentity();
        return new(identity.ActorId, identity.TenantId, identity.BusinessId,
            identity.Permissions);
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (RestaurantMasterForbiddenException error)
        { return Results.Problem(error.Message, statusCode: 403); }
        catch (RestaurantMasterValidationException error)
        { return Results.Problem(error.Message, statusCode: 400); }
        catch (RestaurantMasterConflictException error)
        { return Results.Problem(error.Message, statusCode: 409); }
    }
}
