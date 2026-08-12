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
            return null;

        const string sql = """
            SELECT d.TenantId,
                   d.CredentialSalt,
                   d.CredentialHash,
                   d.CredentialIterations,
                   p.PermissionCode,
                   p.IsGranted
            FROM dbo.EnrolledDevices d
            LEFT JOIN dbo.PosDevicePermissions p ON p.DeviceId=d.DeviceId
            WHERE d.DeviceId=@DeviceId
              AND d.IsActive=1;
            """;

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var tenantId = reader.GetGuid(0);
        var salt = (byte[])reader[1];
        var hash = (byte[])reader[2];
        var iterations = reader.GetInt32(3);
        if (!PosDeviceCredentialHasher.Verify(secret, salt, hash, iterations))
            return null;

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            if (!reader.IsDBNull(4) && reader.GetBoolean(5))
                permissions.Add(reader.GetString(4));
        }
        while (await reader.ReadAsync(cancellationToken));

        return new PosDeviceIdentity(deviceId, tenantId, permissions);
    }
}