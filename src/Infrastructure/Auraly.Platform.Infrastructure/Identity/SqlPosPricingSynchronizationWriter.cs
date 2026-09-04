using System.Data;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlPosPricingSynchronizationWriter(
    ApplicationDbContext context,
    IAuralyIdGenerator ids) : IPosPricingSynchronizationWriter
{
    public async Task EnqueueBusinessesAsync(
        IReadOnlyCollection<Guid> businessIds,
        CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction;
        var ownsTransaction = transaction is null;
        transaction ??= await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        try
        {
            foreach (var businessId in businessIds.Distinct())
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = """
                    DECLARE @Cursor BIGINT;
                    SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
                    FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
                    WHERE BusinessId=@BusinessId AND Stream=N'Configuration';
                    INSERT dbo.PosSynchronizationOutboxMessages(
                      NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                    VALUES(@NotificationId,@BusinessId,N'Configuration',@Cursor,SYSDATETIMEOFFSET());
                    """;
                Add(command, "@NotificationId", ids.NewId());
                Add(command, "@BusinessId", businessId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (ownsTransaction)
                await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (ownsTransaction)
                await transaction.DisposeAsync();
        }
    }

    private static void Add(IDbCommand command, string name, Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.Guid;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
