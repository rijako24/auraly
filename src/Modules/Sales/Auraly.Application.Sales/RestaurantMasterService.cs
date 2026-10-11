using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record RestaurantMasterActor(
    Guid UserId, Guid TenantId, Guid BusinessId,
    IReadOnlySet<string> Permissions);

public interface IRestaurantMasterStore
{
    Task<RestaurantMasterPage<RestaurantRoom>> RoomsAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct);
    Task<RestaurantFloorPlan?> FloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId, bool draft,
        CancellationToken ct);
    Task<RestaurantFloorPlan> SaveFloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId,
        SaveRestaurantFloorPlanRequest request, DateTimeOffset now,
        CancellationToken ct);
    Task<RestaurantFloorPlan> PublishFloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId,
        PublishRestaurantFloorPlanRequest request, DateTimeOffset now,
        CancellationToken ct);
    Task<RestaurantMasterPage<RestaurantTable>> TablesAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct);
    Task<RestaurantCommandConfiguration?> CommandConfigurationAsync(
        RestaurantMasterActor actor, Guid stationId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantPrintDevice>> PrintDevicesAsync(
        RestaurantMasterActor actor, CancellationToken ct);
    Task<RestaurantCommandConfiguration> UpdateCommandConfigurationAsync(
        RestaurantMasterActor actor, Guid stationId,
        UpdateRestaurantCommandConfigurationRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct);
    Task<RestaurantRoom> CreateRoomAsync(
        RestaurantMasterActor actor, CreateRestaurantRoomRequest request,
        DateTimeOffset now, CancellationToken ct);
    Task<RestaurantRoom> UpdateRoomAsync(
        RestaurantMasterActor actor, Guid roomId, UpdateRestaurantRoomRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct);
    Task<RestaurantTable> CreateTableAsync(
        RestaurantMasterActor actor, CreateRestaurantTableRequest request,
        DateTimeOffset now, CancellationToken ct);
    Task<RestaurantTable> UpdateTableAsync(
        RestaurantMasterActor actor, Guid tableId, UpdateRestaurantTableRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct);
    Task<RestaurantMasterPage<RestaurantStation>> StationsAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct);
    Task<RestaurantStation> CreateStationAsync(
        RestaurantMasterActor actor, CreateRestaurantStationRequest request,
        DateTimeOffset now, CancellationToken ct);
    Task<RestaurantStation> UpdateStationAsync(
        RestaurantMasterActor actor, Guid stationId, UpdateRestaurantStationRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct);
    Task<RestaurantMasterPage<RestaurantProductRoute>> ProductRoutesAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct);
    Task<RestaurantProductRoute?> ProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, CancellationToken ct);
    Task<RestaurantProductRoute> SetProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, SetRestaurantProductRouteRequest request,
        byte[]? version, DateTimeOffset now, CancellationToken ct);
    Task<RestaurantProductRoute> DeleteProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, byte[] version,
        DateTimeOffset now, CancellationToken ct);
}

public sealed class RestaurantMasterForbiddenException(string message) : Exception(message);
public sealed class RestaurantMasterValidationException(string message) : Exception(message);
public sealed class RestaurantMasterConflictException(string message) : Exception(message);

public sealed class RestaurantMasterService(
    IRestaurantMasterStore store, TimeProvider time)
{
    public Task<RestaurantMasterPage<RestaurantRoom>> RoomsAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        DemandRead(actor);
        ValidatePage(page, pageSize);
        return store.RoomsAsync(actor, page, pageSize, ct);
    }

    public Task<RestaurantFloorPlan?> FloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId, bool draft,
        CancellationToken ct)
    {
        if (draft) DemandConfigure(actor, actor.BusinessId);
        else DemandRead(actor);
        if (roomId == Guid.Empty)
            throw new RestaurantMasterValidationException("El salón no es válido.");
        return store.FloorPlanAsync(actor, roomId, draft, ct);
    }

    public Task<RestaurantFloorPlan> PublishFloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId,
        PublishRestaurantFloorPlanRequest request, CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        if (roomId == Guid.Empty)
            throw new RestaurantMasterValidationException("El salón no es válido.");
        byte[] version;
        try { version = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new RestaurantMasterValidationException(
            "La versión del salón no es válida."); }
        if (version.Length != 8)
            throw new RestaurantMasterValidationException(
                "La versión del salón no es válida.");
        return store.PublishFloorPlanAsync(actor, roomId, request,
            time.GetUtcNow(), ct);
    }

    public Task<RestaurantFloorPlan> SaveFloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId,
        SaveRestaurantFloorPlanRequest request, CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        if (roomId == Guid.Empty || request.Objects is null ||
            request.Objects.Count > 500 ||
            request.Objects.Select(item => item.ObjectId).Distinct().Count() !=
                request.Objects.Count ||
            request.Objects.Where(item => item.Kind == "Table")
                .Select(item => item.TableId).Distinct().Count() !=
                request.Objects.Count(item => item.Kind == "Table") ||
            request.Objects.Any(item => item.ObjectId == Guid.Empty ||
                item.Kind is not ("Table" or "Wall" or "Door" or "Counter" or "Plant" or "Label"
                    or "Window" or "Column" or "Zone" or "Kitchen" or "Bathroom" or "Entrance"
                    or "Chair" or "Bench") ||
                (item.Kind is "Table" or "Chair" or "Bench") != (item.TableId is not null) ||
                (item.Kind is "Chair" or "Bench") && !request.Objects.Any(table =>
                    table.Kind == "Table" && table.TableId == item.TableId) ||
                item.Shape is not (null or "Round" or "Oval" or "Square" or "Rectangle" or "Rounded") ||
                (item.Kind != "Table" && (item.Shape is not null || item.Chairs is not null)) ||
                item.Chairs is < 0 or > 16 ||
                (item.Color is not null && (item.Color.Length != 7 || item.Color[0] != '#' ||
                    item.Color.Skip(1).Any(c => !Uri.IsHexDigit(c)))) ||
                item.X is < 0 or > 1000 || item.Y is < 0 or > 700 ||
                item.Width < (item.Kind == "Wall" ? 8 : 20) || item.Width > 1000 ||
                item.Height < (item.Kind == "Wall" ? 8 : 20) || item.Height > 700 ||
                item.X + item.Width > 1000 || item.Y + item.Height > 700 ||
                item.Rotation is < 0 or > 359 || item.Label?.Length > 80))
            throw new RestaurantMasterValidationException(
                "El plano tiene objetos, posiciones o mesas no válidos.");
        byte[] version;
        try { version = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new RestaurantMasterValidationException(
            "La versión del salón no es válida."); }
        if (version.Length != 8)
            throw new RestaurantMasterValidationException(
                "La versión del salón no es válida.");
        return store.SaveFloorPlanAsync(actor, roomId, request, time.GetUtcNow(), ct);
    }

    public Task<RestaurantMasterPage<RestaurantTable>> TablesAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        DemandRead(actor);
        ValidatePage(page, pageSize);
        return store.TablesAsync(actor, page, pageSize, ct);
    }

    public Task<RestaurantCommandConfiguration?> CommandConfigurationAsync(
        RestaurantMasterActor actor, Guid stationId, CancellationToken ct)
    {
        DemandRead(actor);
        if (stationId == Guid.Empty)
            throw new RestaurantMasterValidationException("Estación inválida.");
        return store.CommandConfigurationAsync(actor, stationId, ct);
    }

    public Task<IReadOnlyList<RestaurantPrintDevice>> PrintDevicesAsync(
        RestaurantMasterActor actor, CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        return store.PrintDevicesAsync(actor, ct);
    }

    public Task<RestaurantCommandConfiguration> UpdateCommandConfigurationAsync(
        RestaurantMasterActor actor, Guid stationId,
        UpdateRestaurantCommandConfigurationRequest request,
        CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        if (stationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 100 ||
            (request.Mode == "Virtual" &&
                (request.PrinterDeviceId is not null || request.PrinterProfileKey is not null)) ||
            (request.Mode != "Virtual" &&
                (request.Mode is not ("Physical" or "Both") ||
                 (request.PrinterDeviceId is null || request.PrinterDeviceId == Guid.Empty) ||
                 string.IsNullOrWhiteSpace(request.PrinterProfileKey) ||
                 request.PrinterProfileKey.Trim().Length > 64)))
            throw new RestaurantMasterValidationException(
                "La comanda y su impresora no están configuradas correctamente.");
        return store.UpdateCommandConfigurationAsync(actor, stationId,
            request with { Name = request.Name.Trim(),
                PrinterProfileKey = Normalize(request.PrinterProfileKey) },
            DecodeVersion(request.RowVersion), time.GetUtcNow(), ct);
    }

    public Task<RestaurantRoom> CreateRoomAsync(
        RestaurantMasterActor actor, CreateRestaurantRoomRequest request, CancellationToken ct)
    {
        DemandConfigure(actor, request.BusinessId);
        if (request.RoomId == Guid.Empty || request.SortOrder < 0)
            throw new RestaurantMasterValidationException("Identidad y orden del salón inválidos.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            throw new RestaurantMasterValidationException("El nombre del salón debe tener entre 1 y 100 caracteres.");
        return store.CreateRoomAsync(actor, request with { Name = request.Name.Trim() },
            time.GetUtcNow(), ct);
    }

    public Task<RestaurantRoom> UpdateRoomAsync(
        RestaurantMasterActor actor, Guid roomId, UpdateRestaurantRoomRequest request,
        CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        if (roomId == Guid.Empty || request.SortOrder < 0 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            throw new RestaurantMasterValidationException("Datos del salón inválidos.");
        return store.UpdateRoomAsync(actor, roomId,
            request with { Name = request.Name.Trim() }, DecodeVersion(request.RowVersion),
            time.GetUtcNow(), ct);
    }

    public Task<RestaurantTable> CreateTableAsync(
        RestaurantMasterActor actor, CreateRestaurantTableRequest request, CancellationToken ct)
    {
        DemandConfigure(actor, request.BusinessId);
        ValidateTable(request.TableId, request.Code, request.Name, request.Capacity);
        return store.CreateTableAsync(actor,
            request with { Code = request.Code.Trim(), Name = Normalize(request.Name) },
            time.GetUtcNow(), ct);
    }

    public Task<RestaurantTable> UpdateTableAsync(
        RestaurantMasterActor actor, Guid tableId, UpdateRestaurantTableRequest request,
        CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        ValidateTable(tableId, request.Code, request.Name, request.Capacity);
        return store.UpdateTableAsync(actor, tableId,
            request with { Code = request.Code.Trim(), Name = Normalize(request.Name) },
            DecodeVersion(request.RowVersion), time.GetUtcNow(), ct);
    }

    public Task<RestaurantMasterPage<RestaurantStation>> StationsAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        DemandRead(actor);
        ValidatePage(page, pageSize);
        return store.StationsAsync(actor, page, pageSize, ct);
    }

    public Task<RestaurantStation> CreateStationAsync(
        RestaurantMasterActor actor, CreateRestaurantStationRequest request, CancellationToken ct)
    {
        DemandConfigure(actor, request.BusinessId);
        ValidateStation(request.StationId, request.Code, request.Name,
            request.SortOrder, request.Kind, request.TargetMinutes);
        return store.CreateStationAsync(actor, request with
        { Code = request.Code.Trim(), Name = request.Name.Trim() }, time.GetUtcNow(), ct);
    }

    public Task<RestaurantStation> UpdateStationAsync(
        RestaurantMasterActor actor, Guid stationId, UpdateRestaurantStationRequest request,
        CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        ValidateStation(stationId, request.Code, request.Name,
            request.SortOrder, request.Kind, request.TargetMinutes);
        return store.UpdateStationAsync(actor, stationId, request with
        { Code = request.Code.Trim(), Name = request.Name.Trim() },
            DecodeVersion(request.RowVersion), time.GetUtcNow(), ct);
    }

    public Task<RestaurantMasterPage<RestaurantProductRoute>> ProductRoutesAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        DemandRead(actor);
        ValidatePage(page, pageSize);
        return store.ProductRoutesAsync(actor, page, pageSize, ct);
    }

    public Task<RestaurantProductRoute?> ProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, CancellationToken ct)
    {
        DemandRead(actor);
        if (productId == Guid.Empty)
            throw new RestaurantMasterValidationException("Producto inválido.");
        return store.ProductRouteAsync(actor, productId, ct);
    }

    public Task<RestaurantProductRoute> SetProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, SetRestaurantProductRouteRequest request,
        CancellationToken ct)
    {
        DemandConfigure(actor, request.BusinessId);
        if (productId == Guid.Empty || request.StationId == Guid.Empty ||
            request.ShortName?.Trim().Length > 100)
            throw new RestaurantMasterValidationException("Destino del producto inválido.");
        return store.SetProductRouteAsync(actor, productId, request with
        { ShortName = Normalize(request.ShortName) },
            request.RowVersion is null ? null : DecodeVersion(request.RowVersion),
            time.GetUtcNow(), ct);
    }

    public Task<RestaurantProductRoute> DeleteProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, string rowVersion,
        CancellationToken ct)
    {
        DemandConfigure(actor, actor.BusinessId);
        if (productId == Guid.Empty)
            throw new RestaurantMasterValidationException("Producto inválido.");
        return store.DeleteProductRouteAsync(actor, productId,
            DecodeVersion(rowVersion), time.GetUtcNow(), ct);
    }

    private static void ValidateStation(Guid id, string code, string name,
        int sortOrder, string kind, short? targetMinutes)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32 ||
            string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || sortOrder < 0 ||
            kind is not ("Preparation" or "Pickup") || targetMinutes is <= 0)
            throw new RestaurantMasterValidationException("Datos de estación inválidos.");
    }

    private static void DemandRead(RestaurantMasterActor actor)
    {
        if (actor.UserId == Guid.Empty || actor.TenantId == Guid.Empty || actor.BusinessId == Guid.Empty ||
            !new[] { "restaurant.attend", "restaurant.tables.open_without_waiter",
                "restaurant.commands.read", "businesses.update", "sales.create" }
                .Any(actor.Permissions.Contains))
            throw new RestaurantMasterForbiddenException("No tienes acceso a las mesas de esta sede.");
    }

    private static void DemandConfigure(RestaurantMasterActor actor, Guid businessId)
    {
        if (actor.UserId == Guid.Empty || actor.TenantId == Guid.Empty ||
            actor.BusinessId == Guid.Empty || actor.BusinessId != businessId ||
            !actor.Permissions.Contains("businesses.update"))
            throw new RestaurantMasterForbiddenException("No tienes permiso para configurar las mesas de esta sede.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            throw new RestaurantMasterValidationException("Página o tamaño inválido.");
    }

    private static void ValidateTable(Guid id, string code, string? name, short? capacity)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32 ||
            name?.Trim().Length > 100 || capacity is <= 0)
            throw new RestaurantMasterValidationException("Datos de mesa inválidos.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static byte[] DecodeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new RestaurantMasterValidationException("Versión inválida. Vuelve a cargar la ficha.");
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }
        throw new RestaurantMasterValidationException("Versión inválida. Vuelve a cargar la ficha.");
    }
}
