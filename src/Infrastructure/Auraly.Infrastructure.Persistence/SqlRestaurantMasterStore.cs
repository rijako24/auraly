using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantMasterStore(SqlServerConnectionFactory connections)
    : IRestaurantMasterStore
{
    public async Task<RestaurantMasterPage<RestaurantRoom>> RoomsAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*) FROM sales.RestaurantRooms
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId;
            SELECT RoomId,BusinessId,Name,SortOrder,IsActive,RowVersion
            FROM sales.RestaurantRooms
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
            ORDER BY SortOrder,Name,RoomId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (page - 1) * pageSize;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantRoom>(pageSize);
        while (await reader.ReadAsync(ct)) items.Add(ReadRoom(reader));
        return new(items, page, pageSize, count);
    }

    public async Task<RestaurantMasterPage<RestaurantTable>> TablesAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*) FROM sales.RestaurantTables
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId;
            SELECT TableId,BusinessId,RoomId,Code,Name,Capacity,IsActive,RowVersion
            FROM sales.RestaurantTables
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
            ORDER BY Code,TableId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (page - 1) * pageSize;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantTable>(pageSize);
        while (await reader.ReadAsync(ct)) items.Add(ReadTable(reader));
        return new(items, page, pageSize, count);
    }

    public async Task<RestaurantRoom> CreateRoomAsync(
        RestaurantMasterActor actor, CreateRestaurantRoomRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT sales.RestaurantRooms
                (RoomId,TenantId,BusinessId,Name,SortOrder,IsActive,CreatedAt,UpdatedAt)
            OUTPUT inserted.RoomId,inserted.BusinessId,inserted.Name,
                   inserted.SortOrder,inserted.IsActive,inserted.RowVersion
            SELECT @RoomId,@TenantId,@BusinessId,@Name,@SortOrder,1,@Now,@Now
            FROM dbo.Businesses b
            WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId AND b.IsActive=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using var command = new SqlCommand(sql, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@RoomId", request.RoomId);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = request.Name;
            command.Parameters.Add("@SortOrder", SqlDbType.Int).Value = request.SortOrder;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantRoom room;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantMasterForbiddenException("La sede no pertenece a la empresa o está inactiva.");
                room = ReadRoom(reader);
            }
            await AuditAsync(connection, transaction, actor, "RestaurantRoomCreated",
                request.RoomId, null, room, now, ct);
            await transaction.CommitAsync(ct);
            return room;
        }
        catch (SqlException error) when (IsUniqueViolation(error))
        {
            var existing = await FindRoomAsync(connection, actor, request.RoomId, ct);
            if (existing is not null && existing.Name == request.Name &&
                existing.SortOrder == request.SortOrder)
                return existing;
            throw new RestaurantMasterConflictException("El salón ya existe o su nombre está en uso.");
        }
    }

    public async Task<RestaurantRoom> UpdateRoomAsync(
        RestaurantMasterActor actor, Guid roomId, UpdateRestaurantRoomRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE sales.RestaurantRooms
            SET Name=@Name,SortOrder=@SortOrder,IsActive=@IsActive,UpdatedAt=@Now
            OUTPUT deleted.Name,deleted.SortOrder,deleted.IsActive,
                   inserted.RoomId,inserted.BusinessId,inserted.Name,
                   inserted.SortOrder,inserted.IsActive,inserted.RowVersion
            WHERE RoomId=@RoomId AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND RowVersion=@Version
              AND (@IsActive=1 OR NOT EXISTS(
                  SELECT 1 FROM sales.RestaurantTables tableMaster
                  WHERE tableMaster.RoomId=@RoomId AND tableMaster.BusinessId=@BusinessId
                    AND tableMaster.IsActive=1));
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using var command = new SqlCommand(sql, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@RoomId", roomId);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = request.Name;
            command.Parameters.Add("@SortOrder", SqlDbType.Int).Value = request.SortOrder;
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = request.IsActive;
            command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = version;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantRoom room;
            object before;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantMasterConflictException("El salón cambió o no está disponible.");
                before = new { Name = reader.GetString(0), SortOrder = reader.GetInt32(1),
                    IsActive = reader.GetBoolean(2) };
                room = ReadRoom(reader, 3);
            }
            await AuditAsync(connection, transaction, actor, "RestaurantRoomUpdated",
                roomId, before, room, now, ct);
            await transaction.CommitAsync(ct);
            return room;
        }
        catch (SqlException error) when (IsUniqueViolation(error))
        {
            throw new RestaurantMasterConflictException("El nombre del salón ya está en uso.");
        }
    }

    public async Task<RestaurantTable> CreateTableAsync(
        RestaurantMasterActor actor, CreateRestaurantTableRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT sales.RestaurantTables
                (TableId,TenantId,BusinessId,RoomId,Code,Name,Capacity,
                 IsActive,CreatedAt,UpdatedAt)
            OUTPUT inserted.TableId,inserted.BusinessId,inserted.RoomId,
                   inserted.Code,inserted.Name,inserted.Capacity,
                   inserted.IsActive,inserted.RowVersion
            SELECT @TableId,@TenantId,@BusinessId,@RoomId,@Code,@Name,@Capacity,
                   1,@Now,@Now
            FROM dbo.Businesses b
            WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId AND b.IsActive=1
              AND (@RoomId IS NULL OR EXISTS(
                  SELECT 1 FROM sales.RestaurantRooms room
                  WHERE room.RoomId=@RoomId AND room.BusinessId=@BusinessId
                    AND room.TenantId=@TenantId AND room.IsActive=1));
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using var command = new SqlCommand(sql, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@TableId", request.TableId);
            AddTableFields(command, request.RoomId, request.Code, request.Name, request.Capacity);
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantTable table;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantMasterValidationException("La sede o el salón no está disponible.");
                table = ReadTable(reader);
            }
            await AuditAsync(connection, transaction, actor, "RestaurantTableCreated",
                request.TableId, null, table, now, ct);
            await transaction.CommitAsync(ct);
            return table;
        }
        catch (SqlException error) when (IsUniqueViolation(error))
        {
            var existing = await FindTableAsync(connection, actor, request.TableId, ct);
            if (existing is not null && existing.RoomId == request.RoomId &&
                existing.Code == request.Code && existing.Name == request.Name &&
                existing.Capacity == request.Capacity)
                return existing;
            throw new RestaurantMasterConflictException("La mesa ya existe o su código está en uso.");
        }
    }

    public async Task<RestaurantTable> UpdateTableAsync(
        RestaurantMasterActor actor, Guid tableId, UpdateRestaurantTableRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE target
            SET Code=@Code,Name=@Name,Capacity=@Capacity,RoomId=@RoomId,
                IsActive=@IsActive,UpdatedAt=@Now
            OUTPUT deleted.Code,deleted.Name,deleted.Capacity,
                   deleted.RoomId,deleted.IsActive,
                   inserted.TableId,inserted.BusinessId,inserted.RoomId,
                   inserted.Code,inserted.Name,inserted.Capacity,
                   inserted.IsActive,inserted.RowVersion
            FROM sales.RestaurantTables target
            WHERE target.TableId=@TableId AND target.TenantId=@TenantId
              AND target.BusinessId=@BusinessId AND target.RowVersion=@Version
              AND (NOT EXISTS(
                  SELECT 1 FROM sales.RestaurantAccounts account
                  WHERE account.BusinessId=@BusinessId AND account.TableId=@TableId
                    AND account.ReleasedAt IS NULL)
                  OR (@IsActive=1 AND @Code=target.Code
                      AND ((@RoomId IS NULL AND target.RoomId IS NULL)
                           OR @RoomId=target.RoomId)))
              AND (@RoomId IS NULL OR EXISTS(
                  SELECT 1 FROM sales.RestaurantRooms room
                  WHERE room.RoomId=@RoomId AND room.BusinessId=@BusinessId
                    AND room.TenantId=@TenantId AND room.IsActive=1));
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using var command = new SqlCommand(sql, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@TableId", tableId);
            AddTableFields(command, request.RoomId, request.Code, request.Name, request.Capacity);
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = request.IsActive;
            command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = version;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantTable table;
            object before;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantMasterConflictException("La mesa cambió o el salón no está disponible.");
                before = new { Code = reader.GetString(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Capacity = reader.IsDBNull(2) ? (short?)null : reader.GetInt16(2),
                    RoomId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
                    IsActive = reader.GetBoolean(4) };
                table = ReadTable(reader, 5);
            }
            await AuditAsync(connection, transaction, actor, "RestaurantTableUpdated",
                tableId, before, table, now, ct);
            await transaction.CommitAsync(ct);
            return table;
        }
        catch (SqlException error) when (IsUniqueViolation(error))
        {
            throw new RestaurantMasterConflictException("El código de mesa ya está en uso.");
        }
    }

    private static async Task<RestaurantRoom?> FindRoomAsync(
        SqlConnection connection, RestaurantMasterActor actor, Guid roomId, CancellationToken ct)
    {
        const string sql = """
            SELECT RoomId,BusinessId,Name,SortOrder,IsActive,RowVersion
            FROM sales.RestaurantRooms
            WHERE RoomId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@Id", roomId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRoom(reader) : null;
    }

    private static async Task<RestaurantTable?> FindTableAsync(
        SqlConnection connection, RestaurantMasterActor actor, Guid tableId, CancellationToken ct)
    {
        const string sql = """
            SELECT TableId,BusinessId,RoomId,Code,Name,Capacity,IsActive,RowVersion
            FROM sales.RestaurantTables
            WHERE TableId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@Id", tableId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadTable(reader) : null;
    }

    private static RestaurantRoom ReadRoom(SqlDataReader reader, int start = 0) => new(
        reader.GetGuid(start), reader.GetGuid(start + 1), reader.GetString(start + 2),
        reader.GetInt32(start + 3), reader.GetBoolean(start + 4),
        Convert.ToBase64String(reader.GetFieldValue<byte[]>(start + 5)));

    private static RestaurantTable ReadTable(SqlDataReader reader, int start = 0) => new(
        reader.GetGuid(start), reader.GetGuid(start + 1),
        reader.IsDBNull(start + 2) ? null : reader.GetGuid(start + 2),
        reader.GetString(start + 3),
        reader.IsDBNull(start + 4) ? null : reader.GetString(start + 4),
        reader.IsDBNull(start + 5) ? null : reader.GetInt16(start + 5),
        reader.GetBoolean(start + 6),
        Convert.ToBase64String(reader.GetFieldValue<byte[]>(start + 7)));

    private static void AddScope(SqlCommand command, RestaurantMasterActor actor)
    {
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
    }

    private static void AddTableFields(SqlCommand command, Guid? roomId,
        string code, string? name, short? capacity)
    {
        command.Parameters.Add("@RoomId", SqlDbType.UniqueIdentifier).Value =
            (object?)roomId ?? DBNull.Value;
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 32).Value = code;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value =
            (object?)name ?? DBNull.Value;
        command.Parameters.Add("@Capacity", SqlDbType.SmallInt).Value =
            (object?)capacity ?? DBNull.Value;
    }

    private static async Task AuditAsync(SqlConnection connection,
        SqlTransaction transaction, RestaurantMasterActor actor,
        string action, Guid id, object? before, object after,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT dbo.AuditLogs
                (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
                 EntityId,OldValues,NewValues,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,@Action,@EntityType,
                   @EntityId,@OldValues,@NewValues,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.Add("@Action", SqlDbType.NVarChar, 300).Value = action;
        command.Parameters.Add("@EntityType", SqlDbType.NVarChar, 100).Value =
            action.StartsWith("RestaurantRoom", StringComparison.Ordinal) ? "RestaurantRoom" :
            action.StartsWith("RestaurantStation", StringComparison.Ordinal) ? "RestaurantStation" :
            action.StartsWith("RestaurantProductRoute", StringComparison.Ordinal) ? "RestaurantProductRoute" :
            "RestaurantTable";
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = id.ToString();
        command.Parameters.Add("@OldValues", SqlDbType.NVarChar, -1).Value =
            before is null ? DBNull.Value : JsonSerializer.Serialize(before);
        command.Parameters.Add("@NewValues", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(after);
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static bool IsUniqueViolation(SqlException error) =>
        error.Number is 2601 or 2627;
}
