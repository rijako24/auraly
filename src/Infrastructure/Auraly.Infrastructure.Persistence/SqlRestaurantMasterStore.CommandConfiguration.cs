using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantMasterStore
{
    public async Task<IReadOnlyList<RestaurantPrintDevice>> PrintDevicesAsync(
        RestaurantMasterActor actor, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT DeviceId,Name FROM dbo.EnrolledDevices
            WHERE TenantId=@TenantId AND IsActive=1
            ORDER BY Name,DeviceId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        var devices = new List<RestaurantPrintDevice>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            devices.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return devices;
    }

    public async Task<RestaurantCommandConfiguration?> CommandConfigurationAsync(
        RestaurantMasterActor actor, Guid stationId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await ReadCommandConfigurationAsync(connection, null,
            actor, stationId, ct);
    }

    public async Task<RestaurantCommandConfiguration> UpdateCommandConfigurationAsync(
        RestaurantMasterActor actor, Guid stationId,
        UpdateRestaurantCommandConfigurationRequest request,
        byte[] version, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var previous = await ReadCommandConfigurationAsync(connection,
            transaction, actor, stationId, ct) ??
            throw new RestaurantMasterConflictException(
                "La comanda de la estación ya no está disponible.");
        await using var command = new SqlCommand("""
            IF @PrinterDeviceId IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.EnrolledDevices device
              WHERE device.DeviceId=@PrinterDeviceId
                AND device.TenantId=@TenantId AND device.IsActive=1)
              THROW 51122,N'El equipo de impresión no está enrolado o activo.',1;
            UPDATE configuration
            SET Name=@Name,Mode=@Mode,PrinterDeviceId=@PrinterDeviceId,
                PrinterProfileKey=@PrinterProfileKey,UpdatedAt=@Now
            FROM sales.RestaurantCommandConfigurations configuration
            JOIN sales.RestaurantStations station
              ON station.StationId=configuration.StationId
             AND station.BusinessId=configuration.BusinessId
             AND station.IsActive=1
            WHERE configuration.StationId=@StationId
              AND configuration.TenantId=@TenantId
              AND configuration.BusinessId=@BusinessId
              AND configuration.IsActive=1
              AND configuration.RowVersion=@Version;
            IF @@ROWCOUNT<>1
              THROW 51121,N'La comanda cambió. Recarga antes de guardar.',1;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@StationId", stationId);
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = request.Name;
        command.Parameters.Add("@Mode", SqlDbType.NVarChar, 16).Value = request.Mode;
        command.Parameters.Add("@PrinterDeviceId", SqlDbType.UniqueIdentifier).Value =
            (object?)request.PrinterDeviceId ?? DBNull.Value;
        command.Parameters.Add("@PrinterProfileKey", SqlDbType.NVarChar, 64).Value =
            (object?)request.PrinterProfileKey ?? DBNull.Value;
        command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = version;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51122)
        { throw new RestaurantMasterValidationException(error.Message); }
        catch (SqlException error) when (error.Number == 51121)
        { throw new RestaurantMasterConflictException(error.Message); }
        var saved = await ReadCommandConfigurationAsync(connection,
            transaction, actor, stationId, ct) ??
            throw new InvalidOperationException("La comanda guardada desapareció.");
        await AuditAsync(connection, transaction, actor,
            "RestaurantCommandConfigurationUpdated", saved.ConfigurationId,
            previous, saved, now, ct);
        await transaction.CommitAsync(ct);
        return saved;
    }

    private static async Task<RestaurantCommandConfiguration?>
        ReadCommandConfigurationAsync(SqlConnection connection,
            SqlTransaction? transaction, RestaurantMasterActor actor,
            Guid stationId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT configuration.ConfigurationId,configuration.StationId,
                   configuration.Name,configuration.Mode,
                   configuration.PrinterDeviceId,
                   configuration.PrinterProfileKey,
                   configuration.IsActive,configuration.RowVersion
            FROM sales.RestaurantCommandConfigurations configuration
            WHERE configuration.TenantId=@TenantId
              AND configuration.BusinessId=@BusinessId
              AND configuration.StationId=@StationId;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@StationId", stationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetBoolean(6),
            Convert.ToBase64String(reader.GetFieldValue<byte[]>(7)));
    }
}
