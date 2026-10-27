using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class TenantSubscriptionContinuityTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Unpaid_months_create_distinct_orders_without_granting_quota_or_suspending()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var now = new DateTimeOffset(2026,4,15,12,0,0,TimeSpan.Zero);
        var paidEnd = new DateTimeOffset(2026,1,31,12,0,0,TimeSpan.Zero);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await using (var arrange = new SqlCommand("""
                INSERT dbo.Tenants(TenantId,Name,Email) VALUES(@Tenant,N'Cobranza de prueba',CONCAT(@Tenant,N'@example.test'));
                INSERT billing.TenantSubscriptions
                  (TenantSubscriptionId,TenantId,TenantCommercialPlanId,BillingCustomerId,BillingPeriod,Status,
                   CurrentPeriodStart,CurrentPeriodEnd,BillingAnchorDay,FullUserLimit,SellerUserLimit,
                   PosDeviceLimit,DianDocumentMonthlyLimit,PayrollEmployeeLimit,CreatedAt,UpdatedAt)
                SELECT @Subscription,@Tenant,planValue.TenantCommercialPlanId,original.BillingCustomerId,N'Monthly',N'Active',
                  DATEADD(month,-1,@End),@End,31,planValue.IncludedFullUsers,planValue.IncludedSellerUsers,
                  planValue.IncludedPosDevices,planValue.IncludedDianDocuments,planValue.IncludedPayrollEmployees,@Now,@Now
                FROM billing.TenantCommercialPlans planValue
                JOIN billing.BillableServices service ON service.BillableServiceId=planValue.BillableServiceId AND service.Code=N'starter'
                CROSS JOIN billing.TenantSubscriptions original WHERE original.TenantId=@Original;
                """,connection))
            {
                arrange.Parameters.AddWithValue("@Tenant",tenantId);
                arrange.Parameters.AddWithValue("@Subscription",subscriptionId);
                arrange.Parameters.AddWithValue("@Original",fixture.TenantId);
                arrange.Parameters.AddWithValue("@End",paidEnd);
                arrange.Parameters.AddWithValue("@Now",now);
                await arrange.ExecuteNonQueryAsync();
            }
            using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var store = new SqlTenantSubscriptionLifecycleStore(db);
            var process = new TenantSubscriptionLifecycleProcess(store,
                scope.ServiceProvider.GetRequiredService<ITenantCommercialQuoteService>(),
                new FixedTime(now),NullLogger<TenantSubscriptionLifecycleProcess>.Instance);
            for (var i=0;i<5;i++) await process.RunAsync();

            await using var verify = new SqlCommand("""
                SELECT TargetPeriodStart,TargetPeriodEnd FROM billing.TenantSubscriptionRenewalOrders
                WHERE TenantSubscriptionId=@Subscription AND IsCurrent=1 ORDER BY TargetPeriodStart;
                SELECT subscription.Status,subscription.CurrentPeriodEnd,tenant.IsActive,
                  (SELECT COUNT(*) FROM billing.TenantSubscriptionUsagePeriods WHERE TenantSubscriptionId=@Subscription)
                FROM billing.TenantSubscriptions subscription JOIN dbo.Tenants tenant ON tenant.TenantId=subscription.TenantId
                WHERE subscription.TenantSubscriptionId=@Subscription;
                """,connection);
            verify.Parameters.AddWithValue("@Subscription",subscriptionId);
            await using var reader = await verify.ExecuteReaderAsync();
            for(var i=0;i<3;i++)
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(paidEnd.AddMonths(i),reader.GetFieldValue<DateTimeOffset>(0));
                Assert.Equal(paidEnd.AddMonths(i+1),reader.GetFieldValue<DateTimeOffset>(1));
            }
            Assert.False(await reader.ReadAsync());
            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            Assert.Equal("PastDue",reader.GetString(0));
            Assert.Equal(paidEnd,reader.GetFieldValue<DateTimeOffset>(1));
            Assert.True(reader.GetBoolean(2));
            Assert.Equal(0,reader.GetInt32(3));
            await reader.DisposeAsync();
            var current = await new SqlTenantRenewalOrderStore(db).GetCurrentAsync(tenantId,default);
            Assert.NotNull(current);
            Assert.Equal(paidEnd,current.TargetPeriodStart);
            // Simulate a manual deactivation between claiming the existing schedule and applying it.
            await using (var due = new SqlCommand("UPDATE dbo.ScheduledAutomationJobs SET Status=0,ScheduledAtUtc=@Now WHERE TenantSubscriptionId=@Subscription AND JobType=2",connection))
            {
                due.Parameters.AddWithValue("@Now",now.UtcDateTime);
                due.Parameters.AddWithValue("@Subscription",subscriptionId);
                await due.ExecuteNonQueryAsync();
            }
            var claimed = Assert.Single((await store.GetDueAsync(now,default)).Where(value => value.SubscriptionId==subscriptionId));
            await using (var deactivate = new SqlCommand("UPDATE dbo.Tenants SET IsActive=0 WHERE TenantId=@Tenant",connection))
            {
                deactivate.Parameters.AddWithValue("@Tenant",tenantId);
                await deactivate.ExecuteNonQueryAsync();
            }
            var quote = await scope.ServiceProvider.GetRequiredService<ITenantCommercialQuoteService>()
                .QuoteAsync(new("starter","Monthly",0,0,0,0,0),default);
            await store.ApplyAsync(claimed,quote,new("PastDue",null,null,null,false,now.AddDays(1)),now,default);
            Assert.DoesNotContain(await store.GetDueAsync(now,default),value=>value.SubscriptionId==subscriptionId);
            await using (var reactivate = new SqlCommand("UPDATE dbo.Tenants SET IsActive=1 WHERE TenantId=@Tenant",connection))
            {
                reactivate.Parameters.AddWithValue("@Tenant",tenantId);
                await reactivate.ExecuteNonQueryAsync();
            }
            var resumed = Assert.Single((await store.GetDueAsync(now,default)).Where(value=>value.SubscriptionId==subscriptionId));
            Assert.Equal(claimed.ScheduledJobId,resumed.ScheduledJobId);
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE billing.TenantSubscriptionRenewalOrders WHERE TenantSubscriptionId=@Subscription;
                DELETE billing.TenantSubscriptions WHERE TenantSubscriptionId=@Subscription;
                DELETE dbo.Tenants WHERE TenantId=@Tenant;
                """,connection);
            cleanup.Parameters.AddWithValue("@Subscription",subscriptionId);
            cleanup.Parameters.AddWithValue("@Tenant",tenantId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("2026-01-31T12:00:00-05:00", 1, 31, "2026-02-28T12:00:00-05:00")]
    [InlineData("2026-02-28T12:00:00-05:00", 1, 31, "2026-03-31T12:00:00-05:00")]
    [InlineData("2028-02-29T12:00:00-05:00", 12, 29, "2029-02-28T12:00:00-05:00")]
    public async Task Paid_calendar_preserves_the_original_anchor(string start, int months, int anchor, string expected)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT billing.TenantSubscriptionPeriodBoundary(@Start,@Months,@Anchor);", connection);
        command.Parameters.AddWithValue("@Start",DateTimeOffset.Parse(start));
        command.Parameters.AddWithValue("@Months",months);
        command.Parameters.AddWithValue("@Anchor",anchor);
        Assert.Equal(DateTimeOffset.Parse(expected),(DateTimeOffset)(await command.ExecuteScalarAsync())!);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
