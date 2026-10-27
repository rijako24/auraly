using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

internal static class SqlAccountingPosSynchronizationOutbox
{
    public static Task InsertCustomersAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IReadOnlyCollection<Guid> customerIds, IAuralyIdGenerator ids,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customerIds);
        var customers = customerIds.Distinct().ToArray();
        if (businessId == Guid.Empty || customers.Any(id => id == Guid.Empty))
            throw new ArgumentException("Synchronization requires a business and valid customer identities.");
        return InsertAsync(connection, transaction, PosSynchronizationStreams.Customers, customers.Select(customerId =>
            new Notice(ids.NewId(), businessId, customerId)).ToArray(), occurredAt, cancellationToken);
    }

    public static async Task InsertTenantConfigurationAsync(SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, IAuralyIdGenerator ids, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var rows = new List<Notice>();
        await using (var command = new SqlCommand(
            "SELECT BusinessId FROM dbo.Businesses WHERE TenantId=@TenantId ORDER BY BusinessId;", connection, transaction))
        {
            command.Parameters.AddWithValue("@TenantId", tenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(ids.NewId(), reader.GetGuid(0), null));
        }
        await InsertAsync(connection, transaction, PosSynchronizationStreams.Configuration, rows, occurredAt, cancellationToken);
    }

    private sealed record Notice(Guid NotificationId, Guid BusinessId, Guid? CustomerId);

    private static async Task InsertAsync(SqlConnection connection, SqlTransaction transaction,
        string stream, IReadOnlyCollection<Notice> rows, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;
        // One cursor range per affected business; one set insert, even when a
        // financial source changes several customers or a bank serves several sites.
        await using var command = new SqlCommand("""
            DECLARE @Input TABLE(NotificationId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,CustomerId uniqueidentifier);
            INSERT @Input SELECT NotificationId,BusinessId,CustomerId FROM OPENJSON(@Rows)
              WITH(NotificationId uniqueidentifier,BusinessId uniqueidentifier,CustomerId uniqueidentifier);
            IF EXISTS(SELECT 1 FROM @Input input WHERE input.CustomerId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM dbo.Customers customer JOIN dbo.Businesses business ON business.TenantId=customer.TenantId
                WHERE customer.CustomerId=input.CustomerId AND business.BusinessId=input.BusinessId))
              THROW 51608,'A financial customer notification is outside its tenant.',1;
            DECLARE @Businesses TABLE(BusinessId uniqueidentifier PRIMARY KEY);
            INSERT @Businesses SELECT DISTINCT BusinessId FROM @Input;
            DECLARE @Cursors TABLE(BusinessId uniqueidentifier PRIMARY KEY,FirstCursor bigint);
            INSERT @Cursors
            SELECT business.BusinessId,COALESCE(cursorValue.LastCursor,0)+1
            FROM @Businesses business OUTER APPLY(
              SELECT MAX(AvailableThroughCursor) LastCursor
              FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=business.BusinessId AND Stream=@Stream) cursorValue
            ORDER BY business.BusinessId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            INSERT dbo.PosSynchronizationOutboxMessages(
              NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt,EntityType,EntityId,ChangeKind)
            SELECT input.NotificationId,input.BusinessId,@Stream,
              cursorValue.FirstCursor+ROW_NUMBER() OVER(PARTITION BY input.BusinessId ORDER BY input.CustomerId,input.NotificationId)-1,
              @Now,CASE WHEN input.CustomerId IS NOT NULL THEN N'Customer' END,input.CustomerId,
              CASE WHEN input.CustomerId IS NOT NULL THEN N'Upsert' END
            FROM @Input input JOIN @Cursors cursorValue ON cursorValue.BusinessId=input.BusinessId;
            IF @@ROWCOUNT<>@Count THROW 51608,'The financial synchronization batch was not recorded atomically.',1;
            """, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows);
        command.Parameters.Add("@Stream", SqlDbType.NVarChar, 32).Value = stream;
        command.Parameters.AddWithValue("@Now", occurredAt);
        command.Parameters.AddWithValue("@Count", rows.Count);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
