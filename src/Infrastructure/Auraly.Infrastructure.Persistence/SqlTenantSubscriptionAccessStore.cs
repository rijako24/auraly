using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlTenantSubscriptionAccessStore(SqlServerConnectionFactory connections)
{
    public async Task<bool> IsSuspendedAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "dbo.TenantSubscriptionSuspensionGet", connection)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }
}
