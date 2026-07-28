using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosDeviceAuthenticator(SqlServerConnectionFactory connections)
    : IPosDeviceAuthenticator
{
    public async Task<PosDeviceIdentity?> AuthenticateAsync(
        Guid deviceId,
        string secret,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty || string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        const string sql = """
            SELECT d.TenantId,
                   d.BusinessId,
                   d.LocationId,
                   d.WarehouseId,
                   d.RegisterId,
                   d.CredentialSalt,
                   d.CredentialHash,
                   d.CredentialIterations,
                   p.PermissionCode,
                   p.IsGranted
            FROM dbo.PosDevices d
            INNER JOIN dbo.CashRegisters r ON r.RegisterId = d.RegisterId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId = d.WarehouseId
            INNER JOIN dbo.BusinessLocations l ON l.LocationId = d.LocationId
            LEFT JOIN dbo.PosDevicePermissions p ON p.DeviceId = d.DeviceId
            WHERE d.DeviceId = @DeviceId
              AND d.IsActive = 1
              AND r.IsActive = 1
              AND w.IsActive = 1
              AND l.IsActive = 1
              AND r.TenantId = d.TenantId
              AND r.BusinessId = d.BusinessId
              AND r.LocationId = d.LocationId
              AND r.WarehouseId = d.WarehouseId;
            """;

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var tenantId = reader.GetGuid(0);
        var businessId = reader.GetGuid(1);
        var locationId = reader.GetGuid(2);
        var warehouseId = reader.GetGuid(3);
        var registerId = reader.GetGuid(4);
        var salt = (byte[])reader[5];
        var hash = (byte[])reader[6];
        var iterations = reader.GetInt32(7);
        if (!PosDeviceCredentialHasher.Verify(secret, salt, hash, iterations))
        {
            return null;
        }

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            if (!reader.IsDBNull(8) && reader.GetBoolean(9))
            {
                permissions.Add(reader.GetString(8));
            }
        }
        while (await reader.ReadAsync(cancellationToken));

        return new PosDeviceIdentity(
            deviceId,
            tenantId,
            businessId,
            locationId,
            warehouseId,
            registerId,
            permissions);
    }
}

