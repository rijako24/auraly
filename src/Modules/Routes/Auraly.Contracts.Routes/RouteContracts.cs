namespace Auraly.Contracts.Routes;

public static class RoutePermissionCodes
{
    public const string Read = "routes.read";
    public const string Create = "routes.create";
    public const string Update = "routes.update";
    public const string Activate = "routes.activate";
    public const string Deactivate = "routes.deactivate";
    public const string ManageStops = "routes.stops.manage";
    public const string Export = "routes.export";
    public const string ReadAll = "routes.read-all";
    public const string RecordVisits = "routes.visits.record";
    public const string ReadZones = "route-zones.read";
    public const string ManageZones = "route-zones.manage";
}

public sealed record SalesRouteVisit(Guid RouteVisitId, Guid RouteStopId, DateOnly VisitDate, string Status, string? SkipReason, Guid? OrderId, DateTimeOffset OccurredAt, Guid RecordedBy, string? VisitObservation = null);
public sealed record RecordSalesRouteVisitRequest(Guid RouteStopId, DateOnly VisitDate, string Status, string? SkipReason, Guid? OrderId, DateTimeOffset OccurredAt, string IdempotencyKey, string? VisitObservation = null);

public sealed record RouteActorIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record SalesZoneItem(
    Guid ZoneId,
    string Code,
    string Name,
    bool IsActive,
    string RowVersion);

public sealed record CreateSalesZoneRequest(
    Guid BusinessId,
    string Code,
    string Name);

public sealed record RouteScheduleInput(
    int DayOfWeek,
    int RunOrder,
    TimeOnly? PlannedStartTime);

public sealed record CreateSalesRouteRequest(
    Guid BusinessId,
    string Code,
    string Name,
    Guid SellerId,
    Guid? ZoneId,
    string? Notes,
    IReadOnlyCollection<RouteScheduleInput> Schedules);

public sealed record UpdateSalesRouteRequest(
    string Code,
    string Name,
    Guid SellerId,
    Guid? ZoneId,
    string? Notes,
    IReadOnlyCollection<RouteScheduleInput> Schedules,
    string RowVersion);

public sealed record SetSalesRouteStatusRequest(
    bool IsActive,
    string RowVersion);

public sealed record SalesRouteQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? SellerId = null,
    Guid? ZoneId = null,
    int? DayOfWeek = null,
    bool? IsActive = null,
    string? PreparationStatus = null);

public sealed record SalesRouteListItem(
    Guid RouteId,
    string Code,
    string Name,
    Guid SellerId,
    string SellerName,
    Guid? ZoneId,
    string? ZoneName,
    bool IsActive,
    string PreparationStatus,
    int StopCount,
    IReadOnlyCollection<int> Days,
    DateTimeOffset UpdatedAt,
    string RowVersion);

public sealed record SalesRoutePage(
    IReadOnlyCollection<SalesRouteListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record SalesRouteSchedule(
    Guid RouteScheduleId,
    int DayOfWeek,
    int RunOrder,
    TimeOnly? PlannedStartTime);

public sealed record SalesRouteStop(
    Guid RouteStopId,
    Guid CustomerId,
    Guid PartySiteId,
    int Sequence,
    string CustomerName,
    string? Identification,
    string SiteName,
    string AddressLine,
    string? Neighborhood,
    string CityName,
    string? Phone,
    string? GoogleMapsUrl,
    decimal? Latitude,
    decimal? Longitude,
    TimeOnly? PlannedVisitTime,
    string? VisitNote,
    string RowVersion);

public sealed record SalesRouteDetail(
    Guid RouteId,
    Guid BusinessId,
    string Code,
    string Name,
    Guid SellerId,
    string SellerName,
    Guid? ZoneId,
    string? ZoneName,
    string? Notes,
    bool IsActive,
    string PreparationStatus,
    IReadOnlyCollection<SalesRouteSchedule> Schedules,
    IReadOnlyCollection<SalesRouteStop> Stops,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string RowVersion);

public sealed record RouteSellerOption(Guid SellerId, string Code, string Name);
public sealed record RouteOptions(
    IReadOnlyCollection<RouteSellerOption> Sellers,
    IReadOnlyCollection<SalesZoneItem> Zones);

public sealed record RouteCandidateQuery(
    int Page = 1,
    int PageSize = 50,
    string? Search = null,
    Guid? CountryId = null,
    Guid? AdministrativeDivisionId = null,
    Guid? CityId = null,
    string? Neighborhood = null);

public sealed record RouteCandidateSite(
    Guid CustomerId,
    Guid PartySiteId,
    string CustomerName,
    string? Identification,
    string SiteName,
    string AddressLine,
    string? Neighborhood,
    string CityName,
    string? Phone,
    string? GoogleMapsUrl,
    decimal? Latitude,
    decimal? Longitude,
    bool IsAlreadyInRoute,
    bool HasScheduleConflict,
    string? ConflictDescription);

public sealed record RouteCandidatePage(
    IReadOnlyCollection<RouteCandidateSite> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AddRouteStopItem(
    Guid CustomerId,
    Guid PartySiteId,
    string? VisitNote = null,
    TimeOnly? PlannedVisitTime = null);

public sealed record UpdateRouteStopRequest(TimeOnly? PlannedVisitTime, string? VisitNote, string RouteRowVersion);

public sealed record AddRouteStopsRequest(
    IReadOnlyCollection<AddRouteStopItem> Stops,
    string RouteRowVersion);

public sealed record ReorderRouteStopsRequest(
    IReadOnlyCollection<Guid> OrderedStopIds,
    string RouteRowVersion);

public sealed record RouteMutationResult(
    Guid RouteId,
    string RowVersion,
    bool IsActive,
    string PreparationStatus,
    int StopCount);

public sealed class RouteValidationException(string message) : Exception(message);
public sealed class RouteForbiddenException(string message) : Exception(message);
public sealed class RouteNotFoundException(string message) : Exception(message);
public sealed class RouteConflictException(string message) : Exception(message);
