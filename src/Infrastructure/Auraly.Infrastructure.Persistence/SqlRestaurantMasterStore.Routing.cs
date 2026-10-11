using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantMasterStore
{
    public async Task<RestaurantMasterPage<RestaurantStation>> StationsAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*) FROM sales.RestaurantStations
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId;
            SELECT StationId,BusinessId,Code,Name,SortOrder,Kind,TargetMinutes,IsActive,RowVersion
            FROM sales.RestaurantStations
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
            ORDER BY SortOrder,Name,StationId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        AddPage(command, page, pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantStation>(pageSize);
        while (await reader.ReadAsync(ct)) items.Add(ReadStation(reader));
        return new(items, page, pageSize, count);
    }

    public async Task<RestaurantMasterPage<RestaurantProductRoute>> ProductRoutesAsync(
        RestaurantMasterActor actor, int page, int pageSize, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*) FROM sales.RestaurantProductRoutes
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId;
            SELECT route.BusinessId,route.ProductId,route.StationId,
                   route.ShortName,route.RowVersion,product.Name,product.ProductCode
            FROM sales.RestaurantProductRoutes route
            JOIN dbo.Products product
              ON product.ProductId=route.ProductId AND product.TenantId=route.TenantId
            WHERE route.TenantId=@TenantId AND route.BusinessId=@BusinessId
            ORDER BY product.Name,route.ProductId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        AddPage(command, page, pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantProductRoute>(pageSize);
        while (await reader.ReadAsync(ct)) items.Add(ReadRoute(reader));
        return new(items, page, pageSize, count);
    }

    public async Task<RestaurantProductRoute?> ProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, CancellationToken ct)
    {
        const string sql = """
            SELECT route.BusinessId,route.ProductId,route.StationId,
                   route.ShortName,route.RowVersion,product.Name,product.ProductCode
            FROM sales.RestaurantProductRoutes route
            JOIN dbo.Products product
              ON product.ProductId=route.ProductId AND product.TenantId=route.TenantId
            WHERE route.TenantId=@TenantId AND route.BusinessId=@BusinessId
              AND route.ProductId=@ProductId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@ProductId", productId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRoute(reader) : null;
    }

    public async Task<RestaurantStation> CreateStationAsync(
        RestaurantMasterActor actor, CreateRestaurantStationRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT sales.RestaurantStations
                (StationId,TenantId,BusinessId,Code,Name,SortOrder,Kind,TargetMinutes,
                 IsActive,CreatedAt,UpdatedAt)
            OUTPUT inserted.StationId,inserted.BusinessId,inserted.Code,inserted.Name,
                   inserted.SortOrder,inserted.Kind,inserted.TargetMinutes,
                   inserted.IsActive,inserted.RowVersion
            SELECT @StationId,@TenantId,@BusinessId,@Code,@Name,@SortOrder,@Kind,
                   @TargetMinutes,1,@Now,@Now
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
            command.Parameters.AddWithValue("@StationId", request.StationId);
            AddStationFields(command, request.Code, request.Name, request.SortOrder,
                request.Kind, request.TargetMinutes);
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantStation station;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantMasterForbiddenException("La sede no está disponible.");
                station = ReadStation(reader);
            }
            await using (var configuration = new SqlCommand("""
                INSERT sales.RestaurantCommandConfigurations
                  (ConfigurationId,TenantId,BusinessId,StationId,Name,Mode,
                   IsActive,CreatedAt,UpdatedAt)
                VALUES(NEWID(),@TenantId,@BusinessId,@StationId,@Name,
                       N'Virtual',1,@Now,@Now);
                """, connection, transaction))
            {
                AddScope(configuration, actor);
                configuration.Parameters.AddWithValue("@StationId", station.StationId);
                configuration.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = station.Name;
                configuration.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
                await configuration.ExecuteNonQueryAsync(ct);
            }
            await AuditAsync(connection, transaction, actor, "RestaurantStationCreated",
                request.StationId, null, station, now, ct);
            await transaction.CommitAsync(ct);
            return station;
        }
        catch (SqlException error) when (IsUniqueViolation(error))
        {
            throw new RestaurantMasterConflictException("El código de estación ya está en uso.");
        }
    }

    public async Task<RestaurantStation> UpdateStationAsync(
        RestaurantMasterActor actor, Guid stationId, UpdateRestaurantStationRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE sales.RestaurantStations
            SET Code=@Code,Name=@Name,SortOrder=@SortOrder,Kind=@Kind,
                TargetMinutes=@TargetMinutes,IsActive=@IsActive,UpdatedAt=@Now
            OUTPUT deleted.Code,deleted.Name,deleted.SortOrder,deleted.Kind,
                   deleted.TargetMinutes,deleted.IsActive,
                   inserted.StationId,inserted.BusinessId,inserted.Code,inserted.Name,
                   inserted.SortOrder,inserted.Kind,inserted.TargetMinutes,
                   inserted.IsActive,inserted.RowVersion
            WHERE StationId=@StationId AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND RowVersion=@Version
              AND (@IsActive=1 OR NOT EXISTS(
                  SELECT 1 FROM sales.RestaurantProductRoutes route
                  WHERE route.BusinessId=@BusinessId AND route.StationId=@StationId));
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using var command = new SqlCommand(sql, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@StationId", stationId);
            AddStationFields(command, request.Code, request.Name, request.SortOrder,
                request.Kind, request.TargetMinutes);
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = request.IsActive;
            command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = version;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantStation station;
            object before;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantMasterConflictException("La estación cambió o tiene productos asignados.");
                before = new { Code = reader.GetString(0), Name = reader.GetString(1),
                    SortOrder = reader.GetInt32(2), Kind = reader.GetString(3),
                    TargetMinutes = reader.IsDBNull(4) ? (short?)null : reader.GetInt16(4),
                    IsActive = reader.GetBoolean(5) };
                station = ReadStation(reader, 6);
            }
            await AuditAsync(connection, transaction, actor, "RestaurantStationUpdated",
                stationId, before, station, now, ct);
            await transaction.CommitAsync(ct);
            return station;
        }
        catch (SqlException error) when (IsUniqueViolation(error))
        {
            throw new RestaurantMasterConflictException("El código de estación ya está en uso.");
        }
    }

    public async Task<RestaurantProductRoute> SetProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, SetRestaurantProductRouteRequest request,
        byte[]? version, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (
                SELECT 1 FROM dbo.Products p
                JOIN dbo.Businesses b ON b.TenantId=p.TenantId
                JOIN sales.RestaurantStations s ON s.BusinessId=b.BusinessId
                WHERE p.ProductId=@ProductId AND p.TenantId=@TenantId AND p.IsActive=1 AND p.IsIngredient=0
                  AND b.BusinessId=@BusinessId AND b.IsActive=1
                  AND s.StationId=@StationId AND s.IsActive=1)
                THROW 51001, 'Producto o estación no disponible en esta sede.', 1;
            IF @Version IS NULL
            BEGIN
                INSERT sales.RestaurantProductRoutes
                    (BusinessId,TenantId,ProductId,StationId,ShortName,CreatedAt,UpdatedAt)
                OUTPUT inserted.BusinessId,inserted.ProductId,inserted.StationId,
                       inserted.ShortName,inserted.RowVersion
                VALUES (@BusinessId,@TenantId,@ProductId,@StationId,@ShortName,@Now,@Now);
            END
            ELSE
            BEGIN
                UPDATE sales.RestaurantProductRoutes
                SET StationId=@StationId,ShortName=@ShortName,UpdatedAt=@Now
                OUTPUT inserted.BusinessId,inserted.ProductId,inserted.StationId,
                       inserted.ShortName,inserted.RowVersion
                WHERE BusinessId=@BusinessId AND TenantId=@TenantId
                  AND ProductId=@ProductId AND RowVersion=@Version;
            END;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        try
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            RestaurantProductRoute? before = null;
            if (version is not null)
            {
                const string beforeSql = """
                    SELECT BusinessId,ProductId,StationId,ShortName,RowVersion
                    FROM sales.RestaurantProductRoutes
                    WHERE BusinessId=@BusinessId AND TenantId=@TenantId
                      AND ProductId=@ProductId AND RowVersion=@Version;
                    """;
                await using var beforeCommand = new SqlCommand(beforeSql, connection, transaction);
                AddScope(beforeCommand, actor);
                beforeCommand.Parameters.AddWithValue("@ProductId", productId);
                beforeCommand.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = version;
                await using var beforeReader = await beforeCommand.ExecuteReaderAsync(ct);
                if (await beforeReader.ReadAsync(ct)) before = ReadRoute(beforeReader);
            }
            await using var command = new SqlCommand(sql, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@ProductId", productId);
            command.Parameters.AddWithValue("@StationId", request.StationId);
            command.Parameters.Add("@ShortName", SqlDbType.NVarChar, 100).Value =
                (object?)request.ShortName ?? DBNull.Value;
            command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value =
                (object?)version ?? DBNull.Value;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            RestaurantProductRoute route;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantMasterConflictException("El destino del producto cambió. Recarga la configuración.");
                route = ReadRoute(reader);
            }
            await AuditAsync(connection, transaction, actor,
                version is null ? "RestaurantProductRouteCreated" : "RestaurantProductRouteUpdated",
                productId, before, route, now, ct);
            await transaction.CommitAsync(ct);
            return route;
        }
        catch (SqlException error) when (IsUniqueViolation(error))
        {
            throw new RestaurantMasterConflictException("El producto ya tiene un destino en esta sede.");
        }
        catch (SqlException error) when (error.Number == 51001)
        {
            throw new RestaurantMasterValidationException("Producto o estación no disponible en esta sede.");
        }
    }

    public async Task<RestaurantProductRoute> DeleteProductRouteAsync(
        RestaurantMasterActor actor, Guid productId, byte[] version,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            DELETE FROM sales.RestaurantProductRoutes
            OUTPUT deleted.BusinessId,deleted.ProductId,deleted.StationId,
                   deleted.ShortName,deleted.RowVersion
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId
              AND ProductId=@ProductId AND RowVersion=@Version;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = version;
        RestaurantProductRoute removed;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                throw new RestaurantMasterConflictException(
                    "El destino del producto cambió. Recarga la configuración.");
            removed = ReadRoute(reader);
        }
        await AuditAsync(connection, transaction, actor,
            "RestaurantProductRouteDeleted", productId, removed,
            new { Removed = true }, now, ct);
        await transaction.CommitAsync(ct);
        return removed;
    }

    private static RestaurantStation ReadStation(SqlDataReader reader, int start = 0) => new(
        reader.GetGuid(start), reader.GetGuid(start + 1), reader.GetString(start + 2),
        reader.GetString(start + 3), reader.GetInt32(start + 4), reader.GetString(start + 5),
        reader.IsDBNull(start + 6) ? null : reader.GetInt16(start + 6),
        reader.GetBoolean(start + 7),
        Convert.ToBase64String(reader.GetFieldValue<byte[]>(start + 8)));

    private static RestaurantProductRoute ReadRoute(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        Convert.ToBase64String(reader.GetFieldValue<byte[]>(4)),
        reader.FieldCount > 5 ? reader.GetString(5) : null,
        reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null);

    private static void AddStationFields(SqlCommand command, string code, string name,
        int sortOrder, string kind, short? targetMinutes)
    {
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 32).Value = code;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
        command.Parameters.Add("@SortOrder", SqlDbType.Int).Value = sortOrder;
        command.Parameters.Add("@Kind", SqlDbType.NVarChar, 16).Value = kind;
        command.Parameters.Add("@TargetMinutes", SqlDbType.SmallInt).Value =
            (object?)targetMinutes ?? DBNull.Value;
    }

    private static void AddPage(SqlCommand command, int page, int pageSize)
    {
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (page - 1) * pageSize;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
    }
}
