using System.Data;
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
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        await ValidateContextAsync(
            connection, transaction, device, context, cancellationToken);
        await ValidateWorkSessionAsync(
            connection, transaction, device, context, cancellationToken);
        var permissions = await ReadPermissionsAsync(
            connection, transaction, device, context, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new OrderActor(
            context.UserId,
            device.TenantId,
            context.BusinessId,
            context.WorkSessionId,
            device.DeviceId,
            permissions);
    }

    private static async Task ValidateContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PosDeviceIdentity device,
        PosOrderExecutionContext context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT_BIG(1)
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
            WHERE b.BusinessId=@BusinessId
              AND b.TenantId=@TenantId
              AND b.IsActive=1
              AND w.IsActive=1
              AND u.IsActive=1
              AND d.IsActive=1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", device.TenantId);
        command.Parameters.AddWithValue("@DeviceId", device.DeviceId);
        command.Parameters.AddWithValue("@UserId", context.UserId);
        command.Parameters.AddWithValue("@BusinessId", context.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", context.WarehouseId);
        var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (count != 1)
            throw new OrderForbiddenException(
                "El contexto del pedido no pertenece al tenant o no está activo.");
    }

    private static async Task ValidateWorkSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PosDeviceIdentity device,
        PosOrderExecutionContext context,
        CancellationToken cancellationToken)
    {
        await using (var read = new SqlCommand("""
            SELECT COUNT_BIG(1)
            FROM dbo.WorkSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE WorkSessionId=@WorkSessionId
              AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND UserId=@UserId AND DeviceId=@DeviceId AND Status=N'Open';
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("@UserId", context.UserId);
            read.Parameters.AddWithValue("@TenantId", device.TenantId);
            read.Parameters.AddWithValue("@WorkSessionId", context.WorkSessionId);
            read.Parameters.AddWithValue("@BusinessId", context.BusinessId);
            read.Parameters.AddWithValue("@DeviceId", device.DeviceId);
            if (Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new OrderForbiddenException(
                    "La sesión de trabajo local aún no fue sincronizada o no pertenece al contexto del pedido.");
        }
    }

    private static async Task<IReadOnlySet<string>> ReadPermissionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PosDeviceIdentity device,
        PosOrderExecutionContext context,
        CancellationToken cancellationToken)
    {
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
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@UserId", context.UserId);
        command.Parameters.AddWithValue("@TenantId", device.TenantId);
        command.Parameters.AddWithValue("@BusinessId", context.BusinessId);
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
        return permissions;
    }
}
