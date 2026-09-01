using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

internal static class SqlAccountingPosSynchronizationOutbox
{
    public static async Task InsertTenantConfigurationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        IAuralyIdGenerator ids,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var businessIds = new List<Guid>();
        await using (var businesses = new SqlCommand(
            "SELECT BusinessId FROM dbo.Businesses WHERE TenantId=@TenantId ORDER BY BusinessId;",
            connection,
            transaction))
        {
            businesses.Parameters.AddWithValue("@TenantId", tenantId);
            await using var reader = await businesses.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                businessIds.Add(reader.GetGuid(0));
        }

        foreach (var businessId in businessIds)
        {
            await using var notification = new SqlCommand("""
                DECLARE @Cursor BIGINT;
                SELECT @Cursor=COALESCE(MAX(AvailableThroughCursor),0)+1
                FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND Stream=N'Configuration';
                INSERT dbo.PosSynchronizationOutboxMessages(
                    NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                VALUES(@Id,@BusinessId,N'Configuration',@Cursor,@Now);
                """, connection, transaction);
            notification.Parameters.AddWithValue("@Id", ids.NewId());
            notification.Parameters.AddWithValue("@BusinessId", businessId);
            notification.Parameters.AddWithValue("@Now", occurredAt);
            await notification.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
