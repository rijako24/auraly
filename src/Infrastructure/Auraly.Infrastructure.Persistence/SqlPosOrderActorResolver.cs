using Auraly.Application.Orders;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosOrderActorResolver(
    SqlServerConnectionFactory connections) : IPosOrderActorResolver
{
    public async Task<OrderActor> ResolveAsync(
        PosDeviceIdentity device,
        PosOrderExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.UserId == Guid.Empty ||
            context.BusinessId == Guid.Empty ||
            context.WarehouseId == Guid.Empty ||
            context.WorkSessionId == Guid.Empty)
            throw new OrderForbiddenException(
                "Usuario, negocio, bodega y sesión de trabajo son obligatorios.");

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var permissions = await ResolvePermissionsAsync(
            connection, device, context, cancellationToken);
        return new OrderActor(
            context.UserId,
            device.TenantId,
            context.BusinessId,
            context.WorkSessionId,
            device.DeviceId,
            permissions);
    }

    private static async Task<IReadOnlySet<string>> ResolvePermissionsAsync(
        SqlConnection connection,
        PosDeviceIdentity device,
        PosOrderExecutionContext context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.Resource
            FROM dbo.Businesses b
            INNER JOIN dbo.Warehouses w
              ON w.BusinessId=b.BusinessId
             AND w.WarehouseId=@WarehouseId
            INNER JOIN dbo.AppUsers u
              ON u.TenantId=b.TenantId
             AND u.UserId=@UserId
            INNER JOIN dbo.EnrolledDevices d
              ON d.TenantId=b.TenantId
             AND d.DeviceId=@DeviceId
            INNER JOIN dbo.WorkSessions ws
              ON ws.WorkSessionId=@WorkSessionId
             AND ws.TenantId=b.TenantId
             AND ws.BusinessId=b.BusinessId
             AND ws.UserId=u.UserId
             AND ws.DeviceId=d.DeviceId
             AND ws.Status=N'Open'
            LEFT JOIN dbo.UserRoles ur ON ur.UserId=u.UserId
             AND (ur.BusinessId IS NULL OR ur.BusinessId=b.BusinessId)
            LEFT JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1
             AND (r.TenantId IS NULL OR r.TenantId=u.TenantId)
            LEFT JOIN dbo.RolePermissions rp ON rp.RoleId=r.RoleId
            LEFT JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
            WHERE b.BusinessId=@BusinessId
              AND b.TenantId=@TenantId
              AND b.IsActive=1
              AND w.IsActive=1
              AND u.IsActive=1
              AND d.IsActive=1;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", device.TenantId);
        command.Parameters.AddWithValue("@DeviceId", device.DeviceId);
        command.Parameters.AddWithValue("@UserId", context.UserId);
        command.Parameters.AddWithValue("@BusinessId", context.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", context.WarehouseId);
        command.Parameters.AddWithValue("@WorkSessionId", context.WorkSessionId);
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
                "La caja, el usuario o la sesión de trabajo no pertenecen al contexto activo del pedido.");
        return permissions;
    }
}
