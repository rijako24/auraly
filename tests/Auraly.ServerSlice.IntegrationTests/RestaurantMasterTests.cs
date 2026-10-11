using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantMasterTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Floor_plan_saves_scoped_tables_and_rejects_stale_or_foreign_positions()
    {
        using var client = fixture.CreateAdminClient("businesses.update", "restaurant.attend");
        var room = await CreateRoomAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"Plano-{Guid.NewGuid():N}"[..18], 0));
        var other = await CreateRoomAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"Otro-{Guid.NewGuid():N}"[..17], 1));
        var table = await CreateTableAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"P-{Guid.NewGuid():N}"[..10],
                null, 4, room.RoomId));
        var foreignTable = await CreateTableAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"F-{Guid.NewGuid():N}"[..10],
                null, 4, other.RoomId));
        var path = $"/api/commerce/v1/restaurant/rooms/{room.RoomId}/floorplan";
        var initial = await client.GetFromJsonAsync<RestaurantFloorPlan>(path);
        Assert.False(initial!.HasCustomLayout);
        Assert.Contains(initial.Tables, item => item.TableId == table.TableId);

        var objectId = Guid.NewGuid();
        using var savedResponse = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(initial.RowVersion,
                [new(objectId, "Table", table.TableId, 100, 120, 110, 90, 0,
                    null, "Oval", "#0d9488", 6)]));
        savedResponse.EnsureSuccessStatusCode();
        var saved = (await savedResponse.Content.ReadFromJsonAsync<RestaurantFloorPlan>())!;
        Assert.True(saved.HasUnpublishedChanges);
        Assert.True(saved.HasCustomLayout);
        Assert.Single(saved.Objects);
        Assert.Equal(table.TableId, saved.Objects[0].TableId);
        Assert.Equal("Oval", saved.Objects[0].Shape);
        Assert.Equal(6, saved.Objects[0].Chairs);
        var stillPublished = await client.GetFromJsonAsync<RestaurantFloorPlan>(path);
        Assert.False(stillPublished!.HasCustomLayout);
        Assert.Empty(stillPublished.Objects);
        using (var attendant = fixture.CreateAdminClient("restaurant.attend"))
        {
            using var hiddenDraft = await attendant.GetAsync(path + "?draft=true");
            Assert.Equal(HttpStatusCode.Forbidden, hiddenDraft.StatusCode);
            using var visiblePublished = await attendant.GetAsync(path);
            visiblePublished.EnsureSuccessStatusCode();
        }

        using var stale = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(initial.RowVersion, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var foreign = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(saved.RowVersion,
                [new(Guid.NewGuid(), "Table", foreignTable.TableId, 100, 120, 110, 90, 0, null)]));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        using var invalidColor = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(saved.RowVersion,
                [new(Guid.NewGuid(), "Table", table.TableId, 100, 120, 110, 90, 0,
                    null, "Oval", "red", 6)]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidColor.StatusCode);
        var draft = await client.GetFromJsonAsync<RestaurantFloorPlan>(path + "?draft=true");
        Assert.Single(draft!.Objects);
        using var publishedResponse = await client.PostAsJsonAsync(path + "/publish",
            new PublishRestaurantFloorPlanRequest(saved.RowVersion));
        publishedResponse.EnsureSuccessStatusCode();
        var published = (await publishedResponse.Content.ReadFromJsonAsync<RestaurantFloorPlan>())!;
        Assert.False(published.HasUnpublishedChanges);
        Assert.Single(published.Objects);
        var loaded = await client.GetFromJsonAsync<RestaurantFloorPlan>(path);
        Assert.Single(loaded!.Objects);
        using var revisedResponse = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(published.RowVersion,
                [published.Objects[0] with { X = 420 }]));
        revisedResponse.EnsureSuccessStatusCode();
        var revised = (await revisedResponse.Content.ReadFromJsonAsync<RestaurantFloorPlan>())!;
        Assert.True(revised.HasUnpublishedChanges);
        Assert.Equal(420, revised.Objects[0].X);
        var stillOriginal = await client.GetFromJsonAsync<RestaurantFloorPlan>(path);
        Assert.Equal(100, stillOriginal!.Objects[0].X);
        using var secondPublication = await client.PostAsJsonAsync(path + "/publish",
            new PublishRestaurantFloorPlanRequest(revised.RowVersion));
        secondPublication.EnsureSuccessStatusCode();
        var moved = await client.GetFromJsonAsync<RestaurantFloorPlan>(path);
        Assert.Equal(420, moved!.Objects[0].X);
        var overlappingTable = await CreateTableAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId,
                $"O-{Guid.NewGuid():N}"[..10], null, 2, room.RoomId));
        using var collisionDraft = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(moved.RowVersion,
                [moved.Objects[0], new(Guid.NewGuid(), "Table",
                    overlappingTable.TableId, 430, 125, 100, 90, 0, null)]));
        collisionDraft.EnsureSuccessStatusCode();
        var collisionVersion = (await collisionDraft.Content.ReadFromJsonAsync<
            RestaurantFloorPlan>())!.RowVersion;
        using var collisionPublish = await client.PostAsJsonAsync(path + "/publish",
            new PublishRestaurantFloorPlanRequest(collisionVersion));
        Assert.Equal(HttpStatusCode.BadRequest, collisionPublish.StatusCode);
        var stillSafe = await client.GetFromJsonAsync<RestaurantFloorPlan>(path);
        Assert.Single(stillSafe!.Objects);
    }

    [Fact]
    public async Task Floor_plan_keeps_thin_walls_and_individually_positioned_seating()
    {
        using var client = fixture.CreateAdminClient("businesses.update", "restaurant.attend");
        var room = await CreateRoomAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"Sala-{Guid.NewGuid():N}"[..18], 0));
        var table = await CreateTableAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"M-{Guid.NewGuid():N}"[..10],
                null, 4, room.RoomId));
        var path = $"/api/commerce/v1/restaurant/rooms/{room.RoomId}/floorplan";
        var initial = (await client.GetFromJsonAsync<RestaurantFloorPlan>(path))!;
        var objects = new RestaurantFloorPlanObject[] {
            new(Guid.NewGuid(), "Table", table.TableId, 100, 120, 110, 90, 0,
                null, "Rounded", null, 4),
            new(Guid.NewGuid(), "Wall", null, 20, 20, 300, 12, 0, null),
            new(Guid.NewGuid(), "Chair", table.TableId, 240, 130, 38, 38, 90, null),
            new(Guid.NewGuid(), "Bench", table.TableId, 100, 230, 110, 42, 0, null),
        };
        using var save = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(initial.RowVersion, objects));
        save.EnsureSuccessStatusCode();
        var saved = (await save.Content.ReadFromJsonAsync<RestaurantFloorPlan>())!;
        Assert.Equal(4, saved.Objects.Count);
        Assert.Contains(saved.Objects, item => item.Kind == "Wall" && item.Height == 12);
        Assert.Equal(3, saved.Objects.Count(item => item.TableId == table.TableId));

        using var publish = await client.PostAsJsonAsync(path + "/publish",
            new PublishRestaurantFloorPlanRequest(saved.RowVersion));
        publish.EnsureSuccessStatusCode();
        var visible = (await client.GetFromJsonAsync<RestaurantFloorPlan>(path))!;
        Assert.Equal(4, visible.Objects.Count);

        using var orphan = await client.PutAsJsonAsync(path,
            new SaveRestaurantFloorPlanRequest(visible.RowVersion,
                [objects[2]]));
        Assert.Equal(HttpStatusCode.BadRequest, orphan.StatusCode);
    }

    [Fact]
    public async Task Rooms_and_tables_are_scoped_to_business_and_creation_replay_is_audited_once()
    {
        using var client = fixture.CreateAdminClient("businesses.update", "restaurant.attend");
        var roomId = Guid.NewGuid();
        var roomRequest = new CreateRestaurantRoomRequest(roomId, fixture.BusinessId, "Terraza", 2);
        var room = await CreateRoomAsync(client, roomRequest);
        var replay = await CreateRoomAsync(client, roomRequest);
        Assert.Equal(room.RoomId, replay.RoomId);
        Assert.Equal(room.RowVersion, replay.RowVersion);

        var tableId = Guid.NewGuid();
        var tableRequest = new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
            $"T-{tableId:N}"[..10], "Mesa de prueba", 4, roomId);
        var table = await CreateTableAsync(client, tableRequest);
        var tableReplay = await CreateTableAsync(client, tableRequest);
        Assert.Equal(table.RowVersion, tableReplay.RowVersion);
        Assert.Equal(roomId, table.RoomId);

        var rooms = await client.GetFromJsonAsync<RestaurantMasterPage<RestaurantRoom>>(
            "/api/commerce/v1/restaurant/rooms?page=1&pageSize=20");
        var tables = await client.GetFromJsonAsync<RestaurantMasterPage<RestaurantTable>>(
            "/api/commerce/v1/restaurant/tables?page=1&pageSize=20");
        Assert.Contains(rooms!.Items, item => item.RoomId == roomId);
        Assert.Contains(tables!.Items, item => item.TableId == tableId);
        Assert.Equal(1, await CountAuditAsync(roomId, "RestaurantRoomCreated"));
        Assert.Equal(1, await CountAuditAsync(tableId, "RestaurantTableCreated"));
    }

    [Fact]
    public async Task Optimistic_updates_and_duplicate_codes_never_overwrite_a_table()
    {
        using var client = fixture.CreateAdminClient("businesses.update");
        var room = await CreateRoomAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"Sala-{Guid.NewGuid():N}"[..16], 0));
        var table = await CreateTableAsync(client,
            new(Guid.NewGuid(), fixture.BusinessId, $"M-{Guid.NewGuid():N}"[..10],
                "Cuatro personas", 4, room.RoomId));
        using var changed = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/tables/{table.TableId}",
            new UpdateRestaurantTableRequest(table.Code, "Seis personas", 6,
                room.RoomId, true, table.RowVersion));
        changed.EnsureSuccessStatusCode();
        var updated = (await changed.Content.ReadFromJsonAsync<RestaurantTable>())!;
        Assert.Equal((short)6, updated.Capacity);
        Assert.NotEqual(table.RowVersion, updated.RowVersion);
        using var stale = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/tables/{table.TableId}",
            new UpdateRestaurantTableRequest(table.Code, "No guardar", 2,
                room.RoomId, true, table.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var sameCode = new CreateRestaurantTableRequest(Guid.NewGuid(), fixture.BusinessId,
            table.Code, "Duplicada", 2, room.RoomId);
        using var duplicate = await client.PostAsJsonAsync(
            "/api/commerce/v1/restaurant/tables", sameCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, await CountAuditAsync(table.TableId, "RestaurantTableUpdated"));
        using var roomOff = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/rooms/{room.RoomId}",
            new UpdateRestaurantRoomRequest(room.Name, room.SortOrder, false,
                room.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, roomOff.StatusCode);
    }

    [Fact]
    public async Task Waiter_may_read_but_cannot_configure_and_foreign_business_is_rejected()
    {
        using var waiter = fixture.CreateAdminClient("restaurant.attend");
        using var read = await waiter.GetAsync(
            "/api/commerce/v1/restaurant/tables?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var denied = await waiter.PostAsJsonAsync(
            "/api/commerce/v1/restaurant/tables",
            new CreateRestaurantTableRequest(Guid.NewGuid(), fixture.BusinessId,
                $"M-{Guid.NewGuid():N}"[..10]));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var administrator = fixture.CreateAdminClient("businesses.update");
        using var crossed = await administrator.PostAsJsonAsync(
            "/api/commerce/v1/restaurant/tables",
            new CreateRestaurantTableRequest(Guid.NewGuid(), Guid.NewGuid(), "OUTSIDE"));
        Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode);
    }

    [Fact]
    public async Task Product_route_is_scoped_versioned_and_requires_an_active_station()
    {
        using var client = fixture.CreateAdminClient("businesses.update");
        var routeCreatesBefore = await CountAuditAsync(fixture.ProductId,
            "RestaurantProductRouteCreated");
        var routeUpdatesBefore = await CountAuditAsync(fixture.ProductId,
            "RestaurantProductRouteUpdated");
        var routeDeletesBefore = await CountAuditAsync(fixture.ProductId,
            "RestaurantProductRouteDeleted");
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var clear = new SqlCommand("""
                DELETE FROM sales.RestaurantProductRoutes
                WHERE BusinessId=@BusinessId AND ProductId=@ProductId;
                """, connection);
            clear.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            clear.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            await clear.ExecuteNonQueryAsync();
        }
        var stationId = Guid.NewGuid();
        using var created = await client.PostAsJsonAsync(
            "/api/commerce/v1/restaurant/stations",
            new CreateRestaurantStationRequest(stationId, fixture.BusinessId,
                $"K-{stationId:N}"[..10], "Cocina", 0, "Preparation", 15));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var station = (await created.Content.ReadFromJsonAsync<RestaurantStation>())!;

        using var foreignBusiness = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
            new SetRestaurantProductRouteRequest(Guid.NewGuid(), stationId, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, foreignBusiness.StatusCode);
        using var foreignProduct = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/product-routes/{Guid.NewGuid()}",
            new SetRestaurantProductRouteRequest(fixture.BusinessId, stationId, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, foreignProduct.StatusCode);

        using var waiter = fixture.CreateAdminClient("restaurant.attend");
        using var denied = await waiter.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
            new SetRestaurantProductRouteRequest(fixture.BusinessId, stationId, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var assigned = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
            new SetRestaurantProductRouteRequest(fixture.BusinessId, stationId, "Hamburguesa", null));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var route = (await assigned.Content.ReadFromJsonAsync<RestaurantProductRoute>())!;
        Assert.Equal(stationId, route.StationId);
        var loadedRoute = await client.GetFromJsonAsync<RestaurantProductRoute>(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}");
        Assert.Equal(route.RowVersion, loadedRoute!.RowVersion);
        Assert.False(string.IsNullOrWhiteSpace(loadedRoute.ProductName));
        using var replayWithoutVersion = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
            new SetRestaurantProductRouteRequest(fixture.BusinessId, stationId, null, null));
        Assert.Equal(HttpStatusCode.Conflict, replayWithoutVersion.StatusCode);

        using var changed = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
            new SetRestaurantProductRouteRequest(fixture.BusinessId, stationId, "Hamburguesa doble", route.RowVersion));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = (await changed.Content.ReadFromJsonAsync<RestaurantProductRoute>())!;
        using var stale = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
            new SetRestaurantProductRouteRequest(fixture.BusinessId, stationId, "No sobrescribir", route.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var disable = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/stations/{stationId}",
            new UpdateRestaurantStationRequest(station.Code, station.Name, 0,
                station.Kind, station.TargetMinutes, false, station.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, disable.StatusCode);
        using var deniedDelete = await waiter.DeleteAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}?rowVersion={Uri.EscapeDataString(updated.RowVersion)}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedDelete.StatusCode);
        using var staleDelete = await client.DeleteAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}?rowVersion={Uri.EscapeDataString(route.RowVersion)}");
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        using var deleted = await client.DeleteAsync(
            $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}?rowVersion={Uri.EscapeDataString(updated.RowVersion)}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var disabledAfterDelete = await client.PutAsJsonAsync(
            $"/api/commerce/v1/restaurant/stations/{stationId}",
            new UpdateRestaurantStationRequest(station.Code, station.Name, 0,
                station.Kind, station.TargetMinutes, false, station.RowVersion));
        Assert.Equal(HttpStatusCode.OK, disabledAfterDelete.StatusCode);
        Assert.Equal(1, await CountAuditAsync(stationId, "RestaurantStationCreated"));
        Assert.Equal(routeCreatesBefore + 1,
            await CountAuditAsync(fixture.ProductId, "RestaurantProductRouteCreated"));
        Assert.Equal(routeUpdatesBefore + 1,
            await CountAuditAsync(fixture.ProductId, "RestaurantProductRouteUpdated"));
        Assert.Equal(routeDeletesBefore + 1,
            await CountAuditAsync(fixture.ProductId, "RestaurantProductRouteDeleted"));
    }

    private async Task<int> CountAuditAsync(Guid id, string action)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.AuditLogs
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND EntityId=@Id AND Action=@Action;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Id", id.ToString());
        command.Parameters.AddWithValue("@Action", action);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<RestaurantRoom> CreateRoomAsync(
        HttpClient client, CreateRestaurantRoomRequest request)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/restaurant/rooms", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RestaurantRoom>())!;
    }

    private static async Task<RestaurantTable> CreateTableAsync(
        HttpClient client, CreateRestaurantTableRequest request)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/restaurant/tables", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RestaurantTable>())!;
    }
}
