using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Routes;
using Auraly.Domain.Routes;

namespace Auraly.Application.Routes;

public interface IRouteStore
{
    Task<SalesRoutePage> PageAsync(RouteActorIdentity actor, SalesRouteQuery query, CancellationToken ct);
    Task<SalesRouteDetail?> GetAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct);
    Task<RouteOptions> OptionsAsync(RouteActorIdentity actor, CancellationToken ct);
    Task<RouteCandidatePage> CandidateSitesAsync(RouteActorIdentity actor, Guid? routeId, RouteCandidateQuery query, CancellationToken ct);
    Task<SalesZoneItem> CreateZoneAsync(RouteActorIdentity actor, Guid zoneId, string code, string name, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> CreateAsync(RouteActorIdentity actor, Guid routeId, CreateSalesRouteRequest request, string code, string name, string? notes, IReadOnlyList<RouteScheduleInput> schedules, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> UpdateAsync(RouteActorIdentity actor, Guid routeId, UpdateSalesRouteRequest request, string code, string name, string? notes, IReadOnlyList<RouteScheduleInput> schedules, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> SetStatusAsync(RouteActorIdentity actor, Guid routeId, bool isActive, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> AddStopsAsync(RouteActorIdentity actor, Guid routeId, IReadOnlyCollection<(Guid StopId, AddRouteStopItem Stop)> stops, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> UpdateStopAsync(RouteActorIdentity actor, Guid routeId, Guid stopId, UpdateRouteStopRequest request, string? visitNote, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> RemoveStopAsync(RouteActorIdentity actor, Guid routeId, Guid stopId, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<RouteMutationResult> ReorderStopsAsync(RouteActorIdentity actor, Guid routeId, IReadOnlyCollection<Guid> orderedStopIds, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyCollection<SalesRouteVisit>> VisitsAsync(RouteActorIdentity actor, Guid routeId, DateOnly date, CancellationToken ct);
    Task<SalesRouteVisit> RecordVisitAsync(RouteActorIdentity actor, Guid routeId, RecordSalesRouteVisitRequest request, string? reason, string? observation, CancellationToken ct);
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
            throw new RouteValidationException("La página o el tamaño de página están fuera del rango permitido.");
        if (query.DayOfWeek is < 1 or > 7)
            throw new RouteValidationException("El día de la semana debe usar valores del 1 al 7.");
        if (query.PreparationStatus is not null && query.PreparationStatus is not ("Draft" or "Ready" or "AttentionRequired"))
            throw new RouteValidationException("El estado de preparación no es válido.");
        return store.PageAsync(actor, query with { Search = query.Search?.Trim() }, ct);
    }

    public async Task<SalesRouteDetail> GetAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Read);
        Required(routeId, "RouteId");
        return await store.GetAsync(actor, routeId, ct)
            ?? throw new RouteNotFoundException("La ruta no existe en el negocio autenticado.");
    }

    public async Task<SalesRouteDetail> ExportAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Export);
        Required(routeId, "RouteId");
        return await store.GetAsync(actor, routeId, ct)
            ?? throw new RouteNotFoundException("La ruta no existe en el negocio autenticado.");
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
            throw new RouteValidationException("La página o el tamaño de página están fuera del rango permitido.");
        return store.CandidateSitesAsync(actor, routeId, query with
        {
            Search = query.Search?.Trim(),
            Neighborhood = query.Neighborhood?.Trim()
        }, ct);
    }

    public Task<RouteCandidatePage> CustomerSitesAsync(RouteActorIdentity actor, RouteCandidateQuery query, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new RouteValidationException("La página o el tamaño de página están fuera del rango permitido.");
        return store.CandidateSitesAsync(actor, null, query with
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
        if (request.Stops.Count == 0) throw new RouteValidationException("Selecciona al menos un establecimiento del cliente.");
        if (request.Stops.Count > 100) throw new RouteValidationException("Solo se pueden agregar hasta 100 paradas por solicitud.");
        if (request.Stops.Select(stop => stop.PartySiteId).Distinct().Count() != request.Stops.Count)
            throw new RouteValidationException("Un establecimiento del cliente solo puede aparecer una vez en la solicitud.");
        foreach (var stop in request.Stops)
        {
            Required(stop.CustomerId, "CustomerId");
            Required(stop.PartySiteId, "PartySiteId");
            if (stop.VisitNote?.Trim().Length > 300)
                throw new RouteValidationException("La nota de visita no puede superar 300 caracteres.");
        }
        var values = request.Stops.Select(stop => (ids.NewId(), stop with { VisitNote = stop.VisitNote?.Trim() })).ToArray();
        return store.AddStopsAsync(actor, routeId, values, RowVersion(request.RouteRowVersion), time.GetUtcNow(), ct);
    }

    public Task<RouteMutationResult> UpdateStopAsync(RouteActorIdentity actor, Guid routeId, Guid stopId, UpdateRouteStopRequest request, CancellationToken ct)
    {
        Require(actor,RoutePermissionCodes.ManageStops);Required(routeId,"RouteId");Required(stopId,"RouteStopId");
        var note=request.VisitNote?.Trim();if(note?.Length>300)throw new RouteValidationException("La nota de visita no puede superar 300 caracteres.");
        return store.UpdateStopAsync(actor,routeId,stopId,request,note,RowVersion(request.RouteRowVersion),time.GetUtcNow(),ct);
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
            throw new RouteValidationException("Todos los establecimientos de la ruta son obligatorios.");
        if (request.OrderedStopIds.Count != request.OrderedStopIds.Distinct().Count())
            throw new RouteValidationException("El orden de establecimientos no puede contener duplicados.");
        return store.ReorderStopsAsync(actor, routeId, request.OrderedStopIds, RowVersion(request.RouteRowVersion), time.GetUtcNow(), ct);
    }

    public Task<IReadOnlyCollection<SalesRouteVisit>> VisitsAsync(RouteActorIdentity actor, Guid routeId, DateOnly date, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.Read); Required(routeId, "RouteId");
        return store.VisitsAsync(actor, routeId, date, ct);
    }

    public Task<SalesRouteVisit> RecordVisitAsync(RouteActorIdentity actor, Guid routeId, RecordSalesRouteVisitRequest request, CancellationToken ct)
    {
        Require(actor, RoutePermissionCodes.RecordVisits); Required(routeId, "RouteId"); Required(request.RouteStopId, "RouteStopId");
        if (request.Status is not ("Visited" or "Skipped")) throw new RouteValidationException("El estado de la visita debe ser visitado u omitido.");
        var reason = request.SkipReason?.Trim();
        var observation = request.VisitObservation?.Trim();
        if (request.Status == "Skipped" && string.IsNullOrWhiteSpace(reason)) throw new RouteValidationException("Debes indicar el motivo cuando se omite un cliente.");
        if (request.Status == "Visited" && request.OrderId is null) throw new RouteValidationException("Una visita completada requiere el pedido correspondiente.");
        if (reason?.Length > 300) throw new RouteValidationException("El motivo no puede superar 300 caracteres.");
        if (request.Status == "Skipped" && string.IsNullOrWhiteSpace(observation)) throw new RouteValidationException("Debes escribir una observación cuando la visita termina sin pedido.");
        if (observation?.Length > 1000) throw new RouteValidationException("La observación de la visita no puede superar 1.000 caracteres.");
        if (request.Status == "Visited" && observation is not null) throw new RouteValidationException("La observación de visita solo aplica cuando la visita termina sin pedido.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 128) throw new RouteValidationException("La identificación de la operación es obligatoria.");
        return store.RecordVisitAsync(actor, routeId, request with { IdempotencyKey=request.IdempotencyKey.Trim() }, reason, observation, ct);
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
            throw new RouteValidationException("La versión de la ruta no es válida. Actualiza e inténtalo nuevamente.");
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
            throw new RouteForbiddenException("El negocio de la ruta no coincide con el contexto autenticado.");
    }

    private static void Required(Guid value, string field)
    {
        if (value == Guid.Empty) throw new RouteValidationException($"El campo {field} es obligatorio.");
    }

    private static void Require(RouteActorIdentity actor, string permission)
    {
        if (!actor.Permissions.Contains(permission))
            throw new RouteForbiddenException($"Se requiere el permiso '{permission}'.");
    }

    private static void RequireAny(RouteActorIdentity actor, params string[] permissions)
    {
        if (!permissions.Any(actor.Permissions.Contains))
            throw new RouteForbiddenException($"Se requiere uno de estos permisos: {string.Join(", ", permissions)}.");
    }
}
