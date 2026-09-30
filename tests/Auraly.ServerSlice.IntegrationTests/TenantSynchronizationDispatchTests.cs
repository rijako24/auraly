using Auraly.BuildingBlocks.Application.Synchronization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class TenantSynchronizationDispatchTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Tenant_change_wakes_the_pos_outbox_for_another_business()
    {
        var otherBusinessId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = new SqlCommand("""
            INSERT dbo.Businesses
              (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,
               TimeZone,SharesProductPrices,IsActive,CreatedAt)
            SELECT @OtherBusinessId,TenantId,N'Sede señal POS',N'',N'',N'',N'',N'',
                   TimeZone,0,1,SYSUTCDATETIME()
            FROM dbo.Businesses WHERE BusinessId=@BusinessId;
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            VALUES(@NotificationId,@OtherBusinessId,N'Customers',1,SYSUTCDATETIME());
            """, connection))
        {
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
            seed.Parameters.AddWithValue("@NotificationId", notificationId);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            var dispatcher = fixture.Services.GetRequiredService<IPosSynchronizationOutboxDispatcher>();
            await dispatcher.DispatchTenantPendingAsync(fixture.TenantId);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var check = new SqlCommand("""
                    SELECT PublishedAt FROM dbo.PosSynchronizationOutboxMessages
                    WHERE NotificationId=@NotificationId;
                    """, connection);
                check.Parameters.AddWithValue("@NotificationId", notificationId);
                if (await check.ExecuteScalarAsync(timeout.Token) is DateTimeOffset)
                    break;
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE dbo.PosSynchronizationOutboxMessages WHERE NotificationId=@NotificationId;
                DELETE dbo.Businesses WHERE BusinessId=@OtherBusinessId;
                """, connection);
            cleanup.Parameters.AddWithValue("@NotificationId", notificationId);
            cleanup.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
