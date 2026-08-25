using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlFiscalStatusSynchronizationOutbox
{
    public static async Task InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IAuralyIdGenerator ids,
        Guid businessId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Cursor bigint;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'FiscalStatus';

            INSERT dbo.PosSynchronizationOutboxMessages(
                NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            VALUES(@NotificationId,@BusinessId,N'FiscalStatus',@Cursor,@OccurredAt);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@NotificationId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@OccurredAt", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
