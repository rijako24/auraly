using System.Data;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlPosSecuritySynchronizationWriter(
    ApplicationDbContext context) : IPosSecuritySynchronizationWriter
{
    public Task EnqueueUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
        EnqueueAsync(tenantId, userId, null, cancellationToken);

    public Task EnqueueRoleUsersAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken = default) =>
        EnqueueAsync(tenantId, null, roleId, cancellationToken);

    private async Task EnqueueAsync(
        Guid tenantId,
        Guid? userId,
        Guid? roleId,
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
        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "@UserId";
        userParameter.DbType = DbType.Guid;
        userParameter.Value = (object?)userId ?? DBNull.Value;
        command.Parameters.Add(userParameter);
        var roleParameter = command.CreateParameter();
        roleParameter.ParameterName = "@RoleId";
        roleParameter.DbType = DbType.Guid;
        roleParameter.Value = (object?)roleId ?? DBNull.Value;
        command.Parameters.Add(roleParameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
