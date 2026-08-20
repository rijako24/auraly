using System.Security.Claims;
using Auraly.Application.Routes;
using Auraly.Contracts.Routes;

namespace Auraly.Api;

public static class RoutesApi
{
    public static IEndpointRouteBuilder MapRoutesApi(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/commerce/v1/routes").RequireAuthorization("routes.user");

        routes.MapGet("", async (
            ClaimsPrincipal principal, int page, int pageSize, string? search, Guid? sellerId,
            Guid? zoneId, int? dayOfWeek, bool? isActive, string? preparationStatus,
            RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.PageAsync(principal.ToRouteIdentity(), new(
                page == 0 ? 1 : page, pageSize == 0 ? 25 : pageSize, search, sellerId,
                zoneId, dayOfWeek, isActive, preparationStatus), token), Results.Ok));

        routes.MapGet("/options", async (ClaimsPrincipal principal, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.OptionsAsync(principal.ToRouteIdentity(), token), Results.Ok));

        routes.MapGet("/candidate-sites", async (
            ClaimsPrincipal principal, int page, int pageSize, string? search,
            Guid? countryId, Guid? administrativeDivisionId, Guid? cityId, string? neighborhood,
            RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CustomerSitesAsync(principal.ToRouteIdentity(),
                new(page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, search, countryId,
                    administrativeDivisionId, cityId, neighborhood), token), Results.Ok));

        routes.MapGet("/{routeId:guid}", async (ClaimsPrincipal principal, Guid routeId, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetAsync(principal.ToRouteIdentity(), routeId, token), Results.Ok));

        routes.MapGet("/{routeId:guid}/export", async (ClaimsPrincipal principal, Guid routeId, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ExportAsync(principal.ToRouteIdentity(), routeId, token), Results.Ok));

        routes.MapGet("/{routeId:guid}/visits", async (
            ClaimsPrincipal principal, Guid routeId, DateOnly date, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.VisitsAsync(principal.ToRouteIdentity(), routeId, date, token), Results.Ok));

        routes.MapPut("/{routeId:guid}/visits", async (
            ClaimsPrincipal principal, Guid routeId, RecordSalesRouteVisitRequest request,
            RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.RecordVisitAsync(principal.ToRouteIdentity(), routeId, request, token), Results.Ok));

        routes.MapGet("/{routeId:guid}/candidate-sites", async (
            ClaimsPrincipal principal, Guid routeId, int page, int pageSize, string? search,
            Guid? countryId, Guid? administrativeDivisionId, Guid? cityId, string? neighborhood,
            RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CandidateSitesAsync(principal.ToRouteIdentity(), routeId,
                new(page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, search, countryId,
                    administrativeDivisionId, cityId, neighborhood), token), Results.Ok));

        routes.MapPost("", async (ClaimsPrincipal principal, CreateSalesRouteRequest request, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CreateAsync(principal.ToRouteIdentity(), request, token),
                result => Results.Created($"/api/commerce/v1/routes/{result.RouteId:D}", result)));

        routes.MapPut("/{routeId:guid}", async (ClaimsPrincipal principal, Guid routeId, UpdateSalesRouteRequest request, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.UpdateAsync(principal.ToRouteIdentity(), routeId, request, token), Results.Ok));

        routes.MapPost("/{routeId:guid}/status", async (ClaimsPrincipal principal, Guid routeId, SetSalesRouteStatusRequest request, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.SetStatusAsync(principal.ToRouteIdentity(), routeId, request, token), Results.Ok));

        routes.MapPost("/{routeId:guid}/stops", async (ClaimsPrincipal principal, Guid routeId, AddRouteStopsRequest request, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.AddStopsAsync(principal.ToRouteIdentity(), routeId, request, token), Results.Ok));

        routes.MapPut("/{routeId:guid}/stops/{stopId:guid}", async (ClaimsPrincipal principal, Guid routeId, Guid stopId, UpdateRouteStopRequest request, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.UpdateStopAsync(principal.ToRouteIdentity(),routeId,stopId,request,token),Results.Ok));

        routes.MapDelete("/{routeId:guid}/stops/{stopId:guid}", async (
            ClaimsPrincipal principal, Guid routeId, Guid stopId, string rowVersion,
            RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.RemoveStopAsync(principal.ToRouteIdentity(), routeId, stopId, rowVersion, token), Results.Ok));

        routes.MapPut("/{routeId:guid}/stops/order", async (ClaimsPrincipal principal, Guid routeId, ReorderRouteStopsRequest request, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ReorderStopsAsync(principal.ToRouteIdentity(), routeId, request, token), Results.Ok));

        endpoints.MapPost("/api/commerce/v1/route-zones", async (ClaimsPrincipal principal, CreateSalesZoneRequest request, RouteService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CreateZoneAsync(principal.ToRouteIdentity(), request, token),
                result => Results.Created($"/api/commerce/v1/route-zones/{result.ZoneId:D}", result)))
            .RequireAuthorization("routes.user");

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (RouteForbiddenException exception) { return Results.Problem(exception.Message, statusCode: 403); }
        catch (RouteNotFoundException exception) { return Results.Problem(exception.Message, statusCode: 404); }
        catch (RouteValidationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
        catch (RouteConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
    }
}

public static class RouteClaimsPrincipalExtensions
{
    public static RouteActorIdentity ToRouteIdentity(this ClaimsPrincipal principal) => new(
        RequiredGuid(principal, ClaimTypes.NameIdentifier),
        RequiredGuid(principal, "tenant_id"),
        RequiredGuid(principal, "business_id"),
        principal.FindAll("permission").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new RouteForbiddenException($"The authenticated identity lacks claim '{claimType}'.");
}
