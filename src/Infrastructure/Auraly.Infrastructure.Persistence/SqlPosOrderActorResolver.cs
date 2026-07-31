using Auraly.Application.Orders;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosOrderActorResolver(SqlServerConnectionFactory connections)
    : IPosOrderActorResolver
{
    public async Task<OrderActor> ResolveAsync(
        PosDeviceIdentity device,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new OrderForbiddenException("El usuario local de la caja es obligatorio.");

        const string sql = """
            SELECT p.Resource
            FROM dbo.AppUsers u
            LEFT JOIN dbo.UserRoles ur ON ur.UserId=u.UserId
             AND (ur.BusinessId IS NULL OR ur.BusinessId=@BusinessId)
            LEFT JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1
             AND (r.TenantId IS NULL OR r.TenantId=u.TenantId)
            LEFT JOIN dbo.RolePermissions rp ON rp.RoleId=r.RoleId
            LEFT JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
            WHERE u.UserId=@UserId
              AND u.TenantId=@TenantId
              AND u.IsActive=1;
            """;

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", device.TenantId);
        command.Parameters.AddWithValue("@BusinessId", device.BusinessId);
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var found = false;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found = true;
            if (!reader.IsDBNull(0)) permissions.Add(reader.GetString(0));
        }

        if (!found)
            throw new OrderForbiddenException(
                "El usuario local ya no está activo para este tenant.");

        return new OrderActor(
            userId,
            device.TenantId,
            device.BusinessId,
            device.RegisterId,
            device.DeviceId,
            permissions);
    }
}
