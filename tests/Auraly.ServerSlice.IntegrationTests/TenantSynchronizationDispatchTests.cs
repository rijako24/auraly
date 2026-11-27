using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class TenantSynchronizationDispatchTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Tenant_change_wakes_the_pos_outbox_for_another_business()
    {
        var otherBusinessId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var priorNotificationId = Guid.NewGuid();
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
            VALUES(@PriorNotificationId,@OtherBusinessId,N'Customers',1,SYSUTCDATETIME()),
              (@NotificationId,@OtherBusinessId,N'Customers',2,SYSUTCDATETIME());
            """, connection))
        {
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
            seed.Parameters.AddWithValue("@NotificationId", notificationId);
            seed.Parameters.AddWithValue("@PriorNotificationId", priorNotificationId);
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
                    SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
                    WHERE BusinessId=@OtherBusinessId AND PublishedAt IS NOT NULL;
                    """, connection);
                check.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
                if (await check.ExecuteScalarAsync(timeout.Token) is 2)
                    break;
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }

            var gateway = new TestPosSynchronizationPushGateway();
            var factory = fixture.Services.GetRequiredService<SqlServerConnectionFactory>();
            var inlineDispatcher = new SqlPosSynchronizationOutboxDispatcher(
                factory, gateway, TimeProvider.System,
                NullLogger<SqlPosSynchronizationOutboxDispatcher>.Instance, publishInline: true);
            await inlineDispatcher.PublishBusinessAsync(otherBusinessId, timeout.Token);
            Assert.Empty(gateway.Drain());
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@OtherBusinessId;
                DELETE dbo.Businesses WHERE BusinessId=@OtherBusinessId;
                """, connection);
            cleanup.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
