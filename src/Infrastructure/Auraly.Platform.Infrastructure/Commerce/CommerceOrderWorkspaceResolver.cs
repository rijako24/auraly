using System.Data;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Commerce;

public sealed class CommerceOrderWorkspaceResolver(ApplicationDbContext db)
    : ICommerceOrderWorkspaceResolver
{
    public async Task<CommerceOrderWorkspace?> ResolveAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP(1) WarehouseId,Code
                FROM dbo.Warehouses
                WHERE BusinessId=@BusinessId AND IsActive=1 AND UseForSales=1
                ORDER BY CASE WHEN Code=N'VEN' THEN 0 ELSE 1 END,CreatedAt,WarehouseId;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@BusinessId";
            parameter.Value = businessId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new CommerceOrderWorkspace(reader.GetGuid(0), reader.GetString(1))
                : null;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
