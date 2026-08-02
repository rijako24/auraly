using Auraly.Application.Organization;
using Auraly.Contracts.Organization;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesWorkspaceDirectory(SqlServerConnectionFactory connections)
    : ISalesWorkspaceDirectory
{
    public async Task<string?> ResolveTenantNameAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT Name FROM dbo.Tenants WHERE TenantId=@TenantId AND IsActive=1;";
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<IReadOnlyList<SalesWorkspaceOption>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.BusinessId,b.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales,
                   CONVERT(bit,CASE WHEN EXISTS(
                       SELECT 1
                       FROM dbo.PosEnrollmentSessions e
                       INNER JOIN dbo.EnrolledDevices d
                         ON d.DeviceId=e.DeviceId AND d.IsActive=1
                       WHERE e.BusinessId=b.BusinessId AND e.RedeemedAt IS NOT NULL
                   ) THEN 1 ELSE 0 END)
            FROM dbo.Businesses b
            INNER JOIN dbo.Warehouses w
              ON w.BusinessId=b.BusinessId AND w.IsActive=1
            WHERE b.TenantId=@TenantId AND b.IsActive=1
            ORDER BY b.Name,w.Code,w.WarehouseId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var rows = new List<SalesWorkspaceOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SalesWorkspaceOption(
                reader.GetGuid(0), reader.GetString(1),
                reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
                reader.GetBoolean(5), reader.GetBoolean(6)));
        }
        return rows;
    }

    public async Task<SalesWorkspaceContext?> ResolveAsync(
        Guid tenantId,
        SalesWorkspaceSelection selection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.BusinessId,b.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            INNER JOIN dbo.Warehouses w
              ON w.BusinessId=b.BusinessId AND w.IsActive=1
            WHERE b.TenantId=@TenantId
              AND b.BusinessId=@BusinessId
              AND w.WarehouseId=@WarehouseId
              AND b.IsActive=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", selection.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", selection.WarehouseId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SalesWorkspaceContext(
            reader.GetGuid(0), reader.GetString(1),
            reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            reader.GetBoolean(5));
    }
}