using System.Data;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlPosSecuritySynchronizationWriter(
    ApplicationDbContext context) : IPosSecuritySynchronizationWriter
{
    public async Task EnqueueTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "POS security synchronization must be enqueued inside the user transaction.");
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.PosSecuritySynchronizationEnqueue";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@TenantId";
        parameter.DbType = DbType.Guid;
        parameter.Value = tenantId;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
