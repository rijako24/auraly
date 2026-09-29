using System.Data;
using System.Text.Json;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlPosPricingSynchronizationWriter(
    ApplicationDbContext context) : IPosPricingSynchronizationWriter
{
    public async Task EnqueueBusinessesAsync(
        IReadOnlyCollection<Guid> businessIds,
        CancellationToken cancellationToken = default)
    {
        var distinctBusinessIds = businessIds.Distinct().ToArray();
        if (distinctBusinessIds.Length == 0) return;
        var transaction = context.Database.CurrentTransaction;
        var ownsTransaction = transaction is null;
        transaction ??= await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                DECLARE @Target TABLE(BusinessId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
                INSERT @Target SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER,[value])
                FROM OPENJSON(@BusinessIdsJson);
                INSERT dbo.PosSynchronizationOutboxMessages(
                  NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                SELECT NEWID(),target.BusinessId,N'Configuration',ISNULL(latest.CursorValue,0)+1,SYSDATETIMEOFFSET()
                FROM @Target target
                OUTER APPLY(
                  SELECT MAX(AvailableThroughCursor) CursorValue
                  FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=target.BusinessId AND Stream=N'Configuration') latest;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@BusinessIdsJson";
            parameter.DbType = DbType.String;
            parameter.Value = JsonSerializer.Serialize(distinctBusinessIds);
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
            if (ownsTransaction)
                await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (ownsTransaction)
                await transaction.DisposeAsync();
        }
    }

}
