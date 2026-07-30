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
            SELECT b.TenantId,
                   d.BusinessId,
                   d.WarehouseId,
                   d.RegisterId,
                   d.CredentialSalt,
                   d.CredentialHash,
                   d.CredentialIterations,
                   p.PermissionCode,
                   p.IsGranted
            FROM dbo.PosDevices d
            INNER JOIN dbo.CashRegisters r
              ON r.RegisterId=d.RegisterId
             AND r.BusinessId=d.BusinessId
             AND r.WarehouseId=d.WarehouseId
            INNER JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
            INNER JOIN dbo.Warehouses w
              ON w.WarehouseId=d.WarehouseId
             AND w.BusinessId=d.BusinessId
            LEFT JOIN dbo.PosDevicePermissions p ON p.DeviceId=d.DeviceId
            WHERE d.DeviceId=@DeviceId
              AND d.IsActive=1
              AND r.IsActive=1
              AND w.IsActive=1
              AND b.IsActive=1;
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
        var warehouseId = reader.GetGuid(2);
        var registerId = reader.GetGuid(3);
        var salt = (byte[])reader[4];
        var hash = (byte[])reader[5];
        var iterations = reader.GetInt32(6);
        if (!PosDeviceCredentialHasher.Verify(secret, salt, hash, iterations))
        {
            return null;
        }

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            if (!reader.IsDBNull(7) && reader.GetBoolean(8))
            {
                permissions.Add(reader.GetString(7));
            }
        }
        while (await reader.ReadAsync(cancellationToken));

        return new PosDeviceIdentity(
            deviceId,
            tenantId,
            businessId,
            warehouseId,
            registerId,
            permissions);
    }
}
