using Auraly.Application.Organization;
using Auraly.Contracts.Organization;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlOnlineRegisterDirectory(SqlServerConnectionFactory connections)
    : IOnlineRegisterDirectory
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

    public async Task<IReadOnlyList<OnlineRegisterOption>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.BusinessId,b.Name,
                   r.RegisterId,r.Code,r.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales,
                   CONVERT(bit,CASE WHEN EXISTS(
                       SELECT 1 FROM dbo.PosDevices d
                       WHERE d.RegisterId=r.RegisterId AND d.IsActive=1
                   ) THEN 1 ELSE 0 END)
            FROM dbo.Businesses b
            INNER JOIN dbo.CashRegisters r
              ON r.BusinessId=b.BusinessId AND r.IsActive=1
            INNER JOIN dbo.Warehouses w
              ON w.WarehouseId=r.WarehouseId
             AND w.BusinessId=r.BusinessId
             AND w.IsActive=1
            WHERE b.TenantId=@TenantId AND b.IsActive=1
            ORDER BY b.Name,r.Code,r.RegisterId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var rows = new List<OnlineRegisterOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OnlineRegisterOption(
                reader.GetGuid(0), reader.GetString(1),
                reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
                reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
                reader.GetBoolean(8), reader.GetBoolean(9)));
        }
        return rows;
    }

    public async Task<OnlineRegisterContext?> ResolveAsync(
        Guid tenantId,
        OnlineRegisterSelection selection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.BusinessId,b.Name,
                   r.RegisterId,r.Code,r.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            INNER JOIN dbo.CashRegisters r
              ON r.BusinessId=b.BusinessId AND r.IsActive=1
            INNER JOIN dbo.Warehouses w
              ON w.WarehouseId=r.WarehouseId
             AND w.BusinessId=r.BusinessId
             AND w.IsActive=1
            WHERE b.TenantId=@TenantId
              AND b.BusinessId=@BusinessId
              AND r.RegisterId=@RegisterId
              AND b.IsActive=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", selection.BusinessId);
        command.Parameters.AddWithValue("@RegisterId", selection.RegisterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new OnlineRegisterContext(
            reader.GetGuid(0), reader.GetString(1),
            reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
            reader.GetBoolean(8));
    }
}
