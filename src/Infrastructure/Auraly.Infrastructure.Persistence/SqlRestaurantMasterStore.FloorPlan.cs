using System.Data;
using System.Numerics;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantMasterStore
{
    public async Task<RestaurantFloorPlan?> FloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId, bool draft,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await ReadFloorPlanAsync(connection, null, actor, roomId,
            draft, ct);
    }

    public async Task<RestaurantFloorPlan> SaveFloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId,
        SaveRestaurantFloorPlanRequest request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var before = await ReadFloorPlanAsync(connection, transaction,
            actor, roomId, true, ct) ?? throw new RestaurantMasterConflictException(
                "El salón ya no está disponible para editar el plano.");
        var json = JsonSerializer.Serialize(request.Objects);
        await using var command = new SqlCommand("""
            IF EXISTS (
              SELECT 1 FROM OPENJSON(@FloorPlanJson) WITH
                (Kind nvarchar(20),TableId uniqueidentifier) item
              LEFT JOIN sales.RestaurantTables [table]
                ON [table].TableId=item.TableId
               AND [table].BusinessId=@BusinessId
               AND [table].TenantId=@TenantId
               AND [table].RoomId=@RoomId
               AND [table].IsActive=1
              WHERE item.Kind IN (N'Table',N'Chair',N'Bench') AND [table].TableId IS NULL)
              THROW 51120,N'El plano contiene una mesa que no pertenece al salón.',1;
            UPDATE sales.RestaurantRooms
            SET DraftFloorPlanJson=@FloorPlanJson,UpdatedAt=@Now
            WHERE RoomId=@RoomId AND BusinessId=@BusinessId
              AND TenantId=@TenantId AND IsActive=1
              AND RowVersion=@Version;
            IF @@ROWCOUNT<>1
              THROW 51121,N'El salón cambió. Recarga el plano antes de guardar.',1;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RoomId", roomId);
        command.Parameters.Add("@FloorPlanJson", SqlDbType.NVarChar, -1)
            .Value = json;
        command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value =
            Convert.FromBase64String(request.RowVersion);
        command.Parameters.AddWithValue("@Now", now);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51120)
        { throw new RestaurantMasterValidationException(error.Message); }
        catch (SqlException error) when (error.Number == 51121)
        { throw new RestaurantMasterConflictException(error.Message); }
        await AuditAsync(connection, transaction, actor,
            "RestaurantRoomFloorPlanSaved", roomId,
            before.Objects, request.Objects, now, ct);
        var saved = await ReadFloorPlanAsync(connection, transaction,
            actor, roomId, true, ct) ?? throw new InvalidOperationException(
                "El plano guardado no pudo recuperarse.");
        await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task<RestaurantFloorPlan> PublishFloorPlanAsync(
        RestaurantMasterActor actor, Guid roomId,
        PublishRestaurantFloorPlanRequest request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var before = await ReadFloorPlanAsync(connection, transaction,
            actor, roomId, false, ct) ?? throw new RestaurantMasterConflictException(
                "El salón ya no está disponible para publicar el plano.");
        var draft = await ReadFloorPlanAsync(connection, transaction,
            actor, roomId, true, ct) ?? throw new RestaurantMasterConflictException(
                "El borrador del salón ya no está disponible.");
        if (!draft.HasUnpublishedChanges)
            throw new RestaurantMasterConflictException(
                "No hay cambios del plano para publicar.");
        ValidatePublishableGeometry(draft.Objects);
        await using var command = new SqlCommand("""
            IF EXISTS (
              SELECT 1 FROM sales.RestaurantRooms room
              CROSS APPLY OPENJSON(room.DraftFloorPlanJson) WITH
                (Kind nvarchar(20),TableId uniqueidentifier) item
              LEFT JOIN sales.RestaurantTables [table]
                ON [table].TableId=item.TableId
               AND [table].BusinessId=room.BusinessId
               AND [table].TenantId=room.TenantId
               AND [table].RoomId=room.RoomId
               AND [table].IsActive=1
              WHERE room.RoomId=@RoomId AND room.BusinessId=@BusinessId
                AND room.TenantId=@TenantId
                AND item.Kind IN (N'Table',N'Chair',N'Bench') AND [table].TableId IS NULL)
              THROW 51120,N'El plano contiene una mesa que ya no pertenece al salón.',1;
            UPDATE sales.RestaurantRooms
            SET FloorPlanJson=CASE WHEN DraftFloorPlanJson=N'[]'
                                   THEN NULL ELSE DraftFloorPlanJson END,
                DraftFloorPlanJson=NULL,UpdatedAt=@Now
            WHERE RoomId=@RoomId AND BusinessId=@BusinessId
              AND TenantId=@TenantId AND IsActive=1
              AND DraftFloorPlanJson IS NOT NULL
              AND RowVersion=@Version;
            IF @@ROWCOUNT<>1
              THROW 51121,N'El borrador del plano cambió. Recarga antes de publicar.',1;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RoomId", roomId);
        command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value =
            Convert.FromBase64String(request.RowVersion);
        command.Parameters.AddWithValue("@Now", now);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51120)
        { throw new RestaurantMasterValidationException(error.Message); }
        catch (SqlException error) when (error.Number == 51121)
        { throw new RestaurantMasterConflictException(error.Message); }
        var published = await ReadFloorPlanAsync(connection, transaction,
            actor, roomId, false, ct) ?? throw new InvalidOperationException(
                "El plano publicado no pudo recuperarse.");
        await AuditAsync(connection, transaction, actor,
            "RestaurantRoomFloorPlanPublished", roomId,
            before.Objects, published.Objects, now, ct);
        await transaction.CommitAsync(ct);
        return published;
    }

    private static void ValidatePublishableGeometry(
        IReadOnlyList<RestaurantFloorPlanObject> objects)
    {
        var obstacles = objects.Where(item => item.Kind is "Table" or "Wall")
            .Select(item => (item.Kind, Corners: RectangleCorners(item)))
            .ToArray();
        for (var left = 0; left < obstacles.Length; left++)
        for (var right = left + 1; right < obstacles.Length; right++)
        {
            var first = obstacles[left];
            var second = obstacles[right];
            if (first.Kind == "Wall" && second.Kind == "Wall") continue;
            if (RectanglesOverlap(first.Corners, second.Corners))
                throw new RestaurantMasterValidationException(
                    "Una mesa se superpone con otra mesa o una pared. Ajusta el borrador antes de publicar.");
        }
    }

    private static Vector2[] RectangleCorners(RestaurantFloorPlanObject item)
    {
        var angle = item.Rotation * MathF.PI / 180f;
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        var center = new Vector2(item.X + item.Width / 2f,
            item.Y + item.Height / 2f);
        var halfWidth = item.Width / 2f;
        var halfHeight = item.Height / 2f;
        return new Vector2[] {
                new(-halfWidth, -halfHeight), new(halfWidth, -halfHeight),
                new(halfWidth, halfHeight), new(-halfWidth, halfHeight) }
            .Select(point => center + new Vector2(
                point.X * cosine - point.Y * sine,
                point.X * sine + point.Y * cosine))
            .ToArray();
    }

    private static bool RectanglesOverlap(Vector2[] first, Vector2[] second)
    {
        foreach (var corners in new[] { first, second })
        for (var edge = 0; edge < corners.Length; edge++)
        {
            var side = corners[(edge + 1) % corners.Length] - corners[edge];
            var axis = new Vector2(-side.Y, side.X);
            var firstProjection = first.Select(point => Vector2.Dot(point, axis));
            var secondProjection = second.Select(point => Vector2.Dot(point, axis));
            if (firstProjection.Max() <= secondProjection.Min() ||
                secondProjection.Max() <= firstProjection.Min()) return false;
        }
        return true;
    }

    private static async Task<RestaurantFloorPlan?> ReadFloorPlanAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantMasterActor actor, Guid roomId, bool draft,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT room.Name,room.RowVersion,room.FloorPlanJson,
                   room.DraftFloorPlanJson
            FROM sales.RestaurantRooms room
            WHERE room.RoomId=@RoomId AND room.BusinessId=@BusinessId
              AND room.TenantId=@TenantId AND room.IsActive=1;

            SELECT TableId,BusinessId,RoomId,Code,Name,Capacity,
                   IsActive,RowVersion
            FROM sales.RestaurantTables
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId
              AND RoomId=@RoomId AND IsActive=1
            ORDER BY Code,TableId;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RoomId", roomId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var name = reader.GetString(0);
        var version = Convert.ToBase64String(reader.GetFieldValue<byte[]>(1));
        var publishedJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        var draftJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        var json = draft ? draftJson ?? publishedJson : publishedJson;
        RestaurantFloorPlanObject[] objects = json is null ? [] :
            JsonSerializer.Deserialize<RestaurantFloorPlanObject[]>(json) ?? [];
        await reader.NextResultAsync(ct);
        var tables = new List<RestaurantTable>();
        while (await reader.ReadAsync(ct)) tables.Add(ReadTable(reader));
        return new(roomId, name, version, json is not null, objects, tables,
            draft && draftJson is not null);
    }
}
