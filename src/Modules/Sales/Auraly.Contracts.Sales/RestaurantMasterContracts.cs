namespace Auraly.Contracts.Sales;

public sealed record RestaurantRoom(
    Guid RoomId, Guid BusinessId, string Name, int SortOrder, bool IsActive,
    string RowVersion);

public sealed record RestaurantFloorPlanObject(
    Guid ObjectId, string Kind, Guid? TableId,
    int X, int Y, int Width, int Height, int Rotation,
    string? Label, string? Shape = null, string? Color = null,
    int? Chairs = null);

public sealed record RestaurantFloorPlan(
    Guid RoomId, string RoomName, string RowVersion,
    bool HasCustomLayout,
    IReadOnlyList<RestaurantFloorPlanObject> Objects,
    IReadOnlyList<RestaurantTable> Tables,
    bool HasUnpublishedChanges = false);

public sealed record SaveRestaurantFloorPlanRequest(
    string RowVersion,
    IReadOnlyList<RestaurantFloorPlanObject> Objects);

public sealed record PublishRestaurantFloorPlanRequest(string RowVersion);

public sealed record RestaurantTable(
    Guid TableId, Guid BusinessId, Guid? RoomId, string Code, string? Name,
    short? Capacity, bool IsActive, string RowVersion);

public sealed record RestaurantStation(
    Guid StationId, Guid BusinessId, string Code, string Name, int SortOrder,
    string Kind, short? TargetMinutes, bool IsActive, string RowVersion);

public sealed record RestaurantCommandConfiguration(
    Guid ConfigurationId, Guid StationId, string Name, string Mode,
    Guid? PrinterDeviceId, string? PrinterProfileKey,
    bool IsActive, string RowVersion);

public sealed record UpdateRestaurantCommandConfigurationRequest(
    string Name, string Mode, Guid? PrinterDeviceId,
    string? PrinterProfileKey, string RowVersion);

public sealed record RestaurantPrintDevice(Guid DeviceId, string Name);

public sealed record RestaurantProductRoute(
    Guid BusinessId, Guid ProductId, Guid StationId, string? ShortName,
    string RowVersion, string? ProductName = null, string? ProductCode = null);

public sealed record RestaurantMasterPage<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record CreateRestaurantRoomRequest(
    Guid RoomId, Guid BusinessId, string Name, int SortOrder = 0);

public sealed record UpdateRestaurantRoomRequest(
    string Name, int SortOrder, bool IsActive, string RowVersion);

public sealed record CreateRestaurantTableRequest(
    Guid TableId, Guid BusinessId, string Code, string? Name = null,
    short? Capacity = null, Guid? RoomId = null);

public sealed record UpdateRestaurantTableRequest(
    string Code, string? Name, short? Capacity, Guid? RoomId,
    bool IsActive, string RowVersion);

public sealed record CreateRestaurantStationRequest(
    Guid StationId, Guid BusinessId, string Code, string Name, int SortOrder,
    string Kind, short? TargetMinutes);

public sealed record UpdateRestaurantStationRequest(
    string Code, string Name, int SortOrder, string Kind,
    short? TargetMinutes, bool IsActive, string RowVersion);

public sealed record SetRestaurantProductRouteRequest(
    Guid BusinessId, Guid StationId, string? ShortName, string? RowVersion);
