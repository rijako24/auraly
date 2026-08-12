using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Routes;
using Auraly.Domain.Routes;

namespace Auraly.Application.Routes;

public interface IRouteStore
{
    Task<SalesRoutePage> PageAsync(RouteActorIdentity actor, SalesRouteQuery query, CancellationToken ct);
    Task<SalesRouteDetail?> GetAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct);
    Task<RouteOptions> OptionsAsync(RouteActorIdentity actor, CancellationToken ct);
    Task<RouteCandidatePage> CandidateSitesAsync(RouteActorIdentity actor, Guid routeId, RouteCandidateQuery query, CancellationToken ct);
    Task<SalesZoneItem> CreateZoneAsync(RouteActorIdentity actor, Guid zoneId, string code, string name, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> CreateAsync(RouteActorIdentity actor, Guid routeId, CreateSalesRouteRequest request, string code, string name, string? notes, IReadOnlyList<RouteScheduleInput> schedules, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> UpdateAsync(RouteActorIdentity actor, Guid routeId, UpdateSalesRouteRequest request, string code, string name, string? notes, IReadOnlyList<RouteScheduleInput> schedules, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> SetStatusAsync(RouteActorIdentity actor, Guid routeId, bool isActive, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> AddStopsAsync(RouteActorIdentity actor, Guid routeId, IReadOnlyCollection<(Guid StopId, AddRouteStopItem Stop)> stops, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> RemoveStopAsync(RouteActorIdentity actor, Guid routeId, Guid stopId, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> ReorderStopsAsync(RouteActorIdentity actor, Guid routeId, IReadOnlyCollection<Guid> orderedStopIds, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
}

public sealed class RouteService(
    IRouteStore store,
    IAuralyIdGenerator ids,
    TimeProvider time)
{
    public Task<SalesRoutePage> PageAsync(RouteActorIdentity actor, SalesRouteQuery query, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new RouteValidationException("Page and PageSize are outside the allowed range.");
        if (query.DayOfWeek is < 1 or > 7)
            throw new RouteValidationException("DayOfWeek must use ISO values from 1 to 7.");
        if (query.PreparationStatus is not null && query.PreparationStatus is not ("Draft" or "Ready" or "AttentionRequired"))
            throw new RouteValidationException("PreparationStatus is invalid.");
        return store.PageAsync(actor, query with { Search = query.Search?.Trim() }, ct);
    }

    public async Task<SalesRouteDetail> GetAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Read);
        Required(routeId, "RouteId");
        return await store.GetAsync(actor, routeId, ct)
            ?? throw new RouteNotFoundException("The route does not exist in the authenticated business.");
    }

    public async Task<SalesRouteDetail> ExportAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Export);
        Required(routeId, "RouteId");
        return await store.GetAsync(actor, routeId, ct)
            ?? throw new RouteNotFoundException("The route does not exist in the authenticated business.");
    }

    public Task<RouteOptions> OptionsAsync(RouteActorIdentity actor, CancellationToken ct)
    {
        RequireAny(actor, RoutePermissionCodes.Read, RoutePermissionCodes.Create, RoutePermissionCodes.Update);
        return store.OptionsAsync(actor, ct);
    }

    public Task<RouteCandidatePage> CandidateSitesAsync(RouteActorIdentity actor, Guid routeId, RouteCandidateQuery query, CancellationToken ct)
    {
        RequireAny(actor, RoutePermissionCodes.Read, RoutePermissionCodes.ManageStops);
        Required(routeId, "RouteId");
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new RouteValidationException("Page and PageSize are outside the allowed range.");
        return store.CandidateSitesAsync(actor, routeId, query with
        {
            Search = query.Search?.Trim(),
            Neighborhood = query.Neighborhood?.Trim()
        }, ct);
    }

    public Task<SalesZoneItem> CreateZoneAsync(RouteActorIdentity actor, CreateSalesZoneRequest request, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.ManageZones);
        Scope(actor, request.BusinessId);
        var code = Translate(() => RouteRules.NormalizeCode(request.Code));
        var name = Translate(() => RouteRules.NormalizeName(request.Name));
        return store.CreateZoneAsync(actor, ids.NewId(), code, name, time.GetUtcNow(), ct);
    }

    public Task<RouteMutationResult> CreateAsync(RouteActorIdentity actor, CreateSalesRouteRequest request, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Create);
        Scope(actor, request.BusinessId);
        Required(request.SellerId, "SellerId");
        var code = Translate(() => RouteRules.NormalizeCode(request.Code));
        var name = Translate(() => RouteRules.NormalizeName(request.Name));
        var notes = Translate(() => RouteRules.NormalizeNotes(request.Notes));
        var schedules = ValidateSchedules(request.Schedules);
        return store.CreateAsync(actor, ids.NewId(), request, code, name, notes, schedules, time.GetUtcNow(), ct);
    }

    public Task<RouteMutationResult> UpdateAsync(RouteActorIdentity actor, Guid routeId, UpdateSalesRouteRequest request, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Update);
        Required(routeId, "RouteId");
        Required(request.SellerId, "SellerId");
        var code = Translate(() => RouteRules.NormalizeCode(request.Code));
        var name = Translate(() => RouteRules.NormalizeName(request.Name));
        var notes = Translate(() => RouteRules.NormalizeNotes(request.Notes));
        var schedules = ValidateSchedules(request.Schedules);
        return store.UpdateAsync(actor, routeId, request, code, name, notes, schedules, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
    }

    public Task<RouteMutationResult> SetStatusAsync(RouteActorIdentity actor, Guid routeId, SetSalesRouteStatusRequest request, CancellationToken ct)
    {
        Require(actor, request.IsActive ? RoutePermissionCodes.Activate : RoutePermissionCodes.Deactivate);
        Required(routeId, "RouteId");
        return store.SetStatusAsync(actor, routeId, request.IsActive, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
    }

    public Task<RouteMutationResult> AddStopsAsync(RouteActorIdentity actor, Guid routeId, AddRouteStopsRequest request, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.ManageStops);
        Required(routeId, "RouteId");
        if (request.Stops.Count == 0) throw new RouteValidationException("Select at least one customer site.");
        if (request.Stops.Count > 100) throw new RouteValidationException("At most 100 stops can be added per request.");
        if (request.Stops.Select(stop => stop.PartySiteId).Distinct().Count() != request.Stops.Count)
            throw new RouteValidationException("A customer site can only appear once in the request.");
        foreach (var stop in request.Stops)
        {
            Required(stop.CustomerId, "CustomerId");
            Required(stop.PartySiteId, "PartySiteId");
            if (stop.VisitNote?.Trim().Length > 300)
                throw new RouteValidationException("VisitNote cannot exceed 300 characters.");
        }
        var values = request.Stops.Select(stop => (ids.NewId(), stop with { VisitNote = stop.VisitNote?.Trim() })).ToArray();
        return store.AddStopsAsync(actor, routeId, values, RowVersion(request.RouteRowVersion), time.GetUtcNow(), ct);
    }

    public Task<RouteMutationResult> RemoveStopAsync(RouteActorIdentity actor, Guid routeId, Guid stopId, string routeRowVersion, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.ManageStops);
        Required(routeId, "RouteId");
        Required(stopId, "RouteStopId");
        return store.RemoveStopAsync(actor, routeId, stopId, RowVersion(routeRowVersion), time.GetUtcNow(), ct);
    }

    public Task<RouteMutationResult> ReorderStopsAsync(RouteActorIdentity actor, Guid routeId, ReorderRouteStopsRequest request, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.ManageStops);
        Required(routeId, "RouteId");
        if (request.OrderedStopIds.Any(id => id == Guid.Empty))
            throw new RouteValidationException("Every RouteStopId is required.");
        if (request.OrderedStopIds.Count != request.OrderedStopIds.Distinct().Count())
            throw new RouteValidationException("The stop order cannot contain duplicates.");
        return store.ReorderStopsAsync(actor, routeId, request.OrderedStopIds, RowVersion(request.RouteRowVersion), time.GetUtcNow(), ct);
    }

    private static IReadOnlyList<RouteScheduleInput> ValidateSchedules(IReadOnlyCollection<RouteScheduleInput> source)
    {
        var values = Translate(() => RouteRules.Schedules(source.Select(value => (value.DayOfWeek, value.RunOrder, value.PlannedStartTime))));
        return values.Select(value => new RouteScheduleInput(value.DayOfWeek, value.RunOrder, value.PlannedStartTime)).ToArray();
    }

    private static byte[] RowVersion(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new RouteValidationException("RowVersion is invalid.");
        }
    }

    private static T Translate<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArgumentException exception) { throw new RouteValidationException(exception.Message); }
    }

    private static void Scope(RouteActorIdentity actor, Guid businessId)
    {
        if (businessId != actor.BusinessId)
            throw new RouteForbiddenException("The route business does not match the authenticated context.");
    }

    private static void Required(Guid value, string field)
    {
        if (value == Guid.Empty) throw new RouteValidationException($"{field} is required.");
    }

    private static void Require(RouteActorIdentity actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new RouteForbiddenException($"Permission '{permission}' is required.");
    }

    private static void RequireAny(RouteActorIdentity actor, params string[] permissions)
    {
        if (!permissions.Any(actor.Permissions.Contains))
            throw new RouteForbiddenException($"One of these permissions is required: {string.Join(", ", permissions)}.");
    }
}
