using System.Data;
using Auraly.Application.Orders;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosOrderActorResolver(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider) : IPosOrderActorResolver
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
        await EnsureWorkSessionAsync(
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

    private async Task EnsureWorkSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PosDeviceIdentity device,
        PosOrderExecutionContext context,
        CancellationToken cancellationToken)
    {
        await using (var read = new SqlCommand("""
            SELECT WorkSessionId,BusinessId,WarehouseId,DeviceId
            FROM dbo.WorkSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE UserId=@UserId AND Status=N'Open';
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("@UserId", context.UserId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var matches =
                    reader.GetGuid(0) == context.WorkSessionId &&
                    reader.GetGuid(1) == context.BusinessId &&
                    reader.GetGuid(2) == context.WarehouseId &&
                    !reader.IsDBNull(3) &&
                    reader.GetGuid(3) == device.DeviceId;
                if (!matches)
                    throw new OrderForbiddenException(
                        "El usuario ya tiene una sesión de trabajo abierta en otro contexto.");
                return;
            }
        }

        var now = timeProvider.GetUtcNow();
        await using var insert = new SqlCommand("""
            INSERT dbo.WorkSessions
              (WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
               OpenedAt,LastActivityAt,Status)
            VALUES
              (@WorkSessionId,@BusinessId,@WarehouseId,@UserId,@DeviceId,
               @Now,@Now,N'Open');
            """, connection, transaction);
        insert.Parameters.AddWithValue("@WorkSessionId", context.WorkSessionId);
        insert.Parameters.AddWithValue("@BusinessId", context.BusinessId);
        insert.Parameters.AddWithValue("@WarehouseId", context.WarehouseId);
        insert.Parameters.AddWithValue("@UserId", context.UserId);
        insert.Parameters.AddWithValue("@DeviceId", device.DeviceId);
        insert.Parameters.AddWithValue("@Now", now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
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