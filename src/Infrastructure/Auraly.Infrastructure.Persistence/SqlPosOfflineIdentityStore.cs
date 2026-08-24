using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosOfflineIdentityStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider) : IPosOfflineIdentityStore
{
    public async Task<PosOfflineIdentitySnapshot> SnapshotAsync(
        PosIdentityDeviceScope device,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        if (!await DeviceMatchesAsync(connection, device, cancellationToken))
            throw new PosIdentityForbiddenException(
                "El dispositivo y el negocio no pertenecen al mismo tenant activo.");

        const string sql = """
            SELECT u.UserId,u.Username,
                   LTRIM(RTRIM(CONCAT(u.FirstName,N' ',u.LastName))) AS DisplayName,
                   u.PosOfflinePasswordSalt,u.PosOfflinePasswordHash,
                   u.PosOfflinePasswordIterations,u.PosOfflinePasswordChangedAt,
                   credential.SecretSalt,credential.SecretHash,
                   credential.SecretIterations,credential.CreatedAt,credential.IsOneTime,
                   p.Resource
            FROM dbo.AppUsers u
            LEFT JOIN dbo.SupervisorCredentials credential
              ON credential.UserId=u.UserId AND credential.IsActive=1
              AND (credential.ValidUntil IS NULL OR credential.ValidUntil>SYSUTCDATETIME())
            JOIN dbo.UserRoles ur ON ur.UserId=u.UserId
                AND (ur.BusinessId IS NULL OR ur.BusinessId=@BusinessId)
            JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1
                AND (r.TenantId IS NULL OR r.TenantId=@TenantId)
            JOIN dbo.RolePermissions rp ON rp.RoleId=r.RoleId
            JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
            WHERE u.TenantId=@TenantId
              AND u.IsActive=1
              AND u.PosOfflinePasswordSalt IS NOT NULL
              AND u.PosOfflinePasswordHash IS NOT NULL
              AND u.PosOfflinePasswordIterations IS NOT NULL
              AND u.PosOfflinePasswordChangedAt IS NOT NULL
              AND EXISTS (
                  SELECT 1
                  FROM dbo.UserRoles salesUr
                  JOIN dbo.AppRoles salesRole
                    ON salesRole.RoleId=salesUr.RoleId AND salesRole.IsActive=1
                    AND (salesRole.TenantId IS NULL OR salesRole.TenantId=@TenantId)
                  JOIN dbo.RolePermissions salesRp ON salesRp.RoleId=salesRole.RoleId
                  JOIN dbo.Permissions salesPermission
                    ON salesPermission.PermissionId=salesRp.PermissionId
                  WHERE salesUr.UserId=u.UserId
                    AND (salesUr.BusinessId IS NULL OR salesUr.BusinessId=@BusinessId)
                    AND salesPermission.Resource=@SalesCreate)
            ORDER BY u.UserId,p.Resource;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", device.TenantId);
        command.Parameters.AddWithValue("@BusinessId", device.BusinessId);
        command.Parameters.AddWithValue(
            "@SalesCreate", CommercePermissionCodes.SalesCreate);

        var users = new Dictionary<Guid, MutableUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var userId = reader.GetGuid(0);
            if (!users.TryGetValue(userId, out var user))
            {
                var username = reader.GetString(1);
                var displayName = reader.GetString(2);
                user = new MutableUser(
                    userId,
                    username,
                    string.IsNullOrWhiteSpace(displayName) ? username : displayName,
                    new PosOfflinePasswordVerifier(
                        (byte[])reader[3],
                        (byte[])reader[4],
                        reader.GetInt32(5),
                        reader.GetFieldValue<DateTimeOffset>(6)),
                    reader.IsDBNull(7)
                        ? null
                        : new PosOfflineSupervisorCredentialVerifier(
                            (byte[])reader[7],
                            (byte[])reader[8],
                            reader.GetInt32(9),
                            reader.GetFieldValue<DateTimeOffset>(10),
                            reader.GetBoolean(11)));
                users.Add(userId, user);
            }
            user.Permissions.Add(reader.GetString(12));
        }

        var projections = users.Values
            .Select(user => new PosOfflineUserProjection(
                user.UserId,
                user.Username,
                user.DisplayName,
                user.Permissions.Order(StringComparer.Ordinal).ToArray(),
                user.Verifier,
                user.SupervisorCredential))
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var now = timeProvider.GetUtcNow();
        return new PosOfflineIdentitySnapshot(
            Revision(projections),
            now,
            now.AddDays(7),
            projections);
    }

    private static async Task<bool> DeviceMatchesAsync(
        SqlConnection connection,
        PosIdentityDeviceScope device,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.EnrolledDevices d
            JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
              AND b.TenantId=d.TenantId AND b.IsActive=1
            WHERE d.DeviceId=@DeviceId AND d.IsActive=1
              AND d.TenantId=@TenantId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DeviceId", device.DeviceId);
        command.Parameters.AddWithValue("@BusinessId", device.BusinessId);
        command.Parameters.AddWithValue("@TenantId", device.TenantId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static string Revision(
        IReadOnlyList<PosOfflineUserProjection> projections)
    {
        var canonical = new StringBuilder();
        foreach (var user in projections)
        {
            canonical
                .Append(user.UserId.ToString("N")).Append('|')
                .Append(user.Username).Append('|')
                .Append(user.DisplayName).Append('|')
                .Append(Convert.ToBase64String(user.PasswordVerifier.Salt)).Append('|')
                .Append(Convert.ToBase64String(user.PasswordVerifier.Hash)).Append('|')
                .Append(user.PasswordVerifier.Iterations).Append('|')
                .Append(user.PasswordVerifier.ChangedAt.ToUniversalTime().Ticks)
                .Append('|');
            if (user.SupervisorCredential is not null)
            {
                canonical
                    .Append(Convert.ToBase64String(user.SupervisorCredential.Salt)).Append('|')
                    .Append(Convert.ToBase64String(user.SupervisorCredential.Hash)).Append('|')
                    .Append(user.SupervisorCredential.Iterations).Append('|')
                    .Append(user.SupervisorCredential.ChangedAt.ToUniversalTime().Ticks).Append('|')
                    .Append(user.SupervisorCredential.IsOneTime);
            }
            canonical
                .Append('|')
                .AppendJoin(',', user.Permissions)
                .AppendLine();
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private sealed record MutableUser(
        Guid UserId,
        string Username,
        string DisplayName,
        PosOfflinePasswordVerifier Verifier,
        PosOfflineSupervisorCredentialVerifier? SupervisorCredential)
    {
        public HashSet<string> Permissions { get; } =
            new(StringComparer.Ordinal);
    }
}
