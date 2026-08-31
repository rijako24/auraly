using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantBillingNotificationStore(ApplicationDbContext db)
    : ITenantBillingNotificationStore
{
    public async Task<IReadOnlyList<TenantBillingNotificationDto>> GetAsync(
        Guid tenantId, Guid userId, int take, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP(@Take) TenantBillingNotificationId,TenantSubscriptionRenewalOrderId,
                   EventKey,Title,Message,ActionUrl,CreatedAt,ReadAt
            FROM billing.TenantBillingNotifications
            WHERE TenantId=@TenantId AND UserId=@UserId
            ORDER BY CreatedAt DESC,TenantBillingNotificationId DESC;
            """, connection);
        command.Parameters.AddWithValue("@Take", Math.Clamp(take, 1, 100));
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        var result = new List<TenantBillingNotificationDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6), reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        return result;
    }

    public async Task MarkReadAsync(
        Guid tenantId, Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE billing.TenantBillingNotifications
            SET ReadAt=COALESCE(ReadAt,SYSDATETIMEOFFSET())
            WHERE TenantBillingNotificationId=@NotificationId AND TenantId=@TenantId AND UserId=@UserId;
            """, connection);
        command.Parameters.AddWithValue("@NotificationId", notificationId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
