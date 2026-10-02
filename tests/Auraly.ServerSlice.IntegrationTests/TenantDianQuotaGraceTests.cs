using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class TenantDianQuotaGraceTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData("Monthly", 1)]
    [InlineData("Annual", 12)]
    public async Task Expired_period_opens_grace_capacity_and_only_reports_exhaustion_at_the_limit(
        string billingPeriod, int months)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var end = now.AddMinutes(-1);
        try
        {
            await using (var arrange = new SqlCommand("""
                UPDATE billing.TenantSubscriptions
                SET BillingPeriod=@BillingPeriod,Status=N'PastDue',
                    CurrentPeriodStart=DATEADD(month,-@Months,@End),CurrentPeriodEnd=@End,
                    DianDocumentMonthlyLimit=1
                WHERE TenantId=@TenantId;
                UPDATE billing.TenantSubscriptionUsagePeriods
                SET PeriodStart=DATEADD(month,-@Months,@End),PeriodEnd=@End,
                    DianDocumentsUsed=0
                WHERE TenantSubscriptionId=(SELECT TenantSubscriptionId
                    FROM billing.TenantSubscriptions WHERE TenantId=@TenantId);
                """, connection, transaction))
            {
                arrange.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                arrange.Parameters.AddWithValue("@End", end);
                arrange.Parameters.AddWithValue("@BillingPeriod", billingPeriod);
                arrange.Parameters.AddWithValue("@Months", months);
                await arrange.ExecuteNonQueryAsync();
            }

            var firstDocumentId = Guid.NewGuid();
            var first = await ReserveAsync(firstDocumentId, now);
            Assert.True(first.Reserved);
            Assert.Null(first.Reason);
            Assert.True((await ReserveAsync(firstDocumentId, now)).Reserved);
            var second = await ReserveAsync(Guid.NewGuid(), now);
            Assert.False(second.Reserved);
            Assert.Equal("QuotaExhausted", second.Reason);

            await using (var verify = new SqlCommand("""
                SELECT DianDocumentsUsed FROM billing.TenantSubscriptionUsagePeriods
                WHERE TenantSubscriptionId=(SELECT TenantSubscriptionId
                    FROM billing.TenantSubscriptions WHERE TenantId=@TenantId)
                  AND PeriodStart<=@Now AND PeriodEnd>@Now;
                """, connection, transaction))
            {
                verify.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                verify.Parameters.AddWithValue("@Now", now);
                Assert.Equal(1, Convert.ToInt32(await verify.ExecuteScalarAsync()));
            }

            Assert.False(await IsBlockedAsync());
            await using (var expire = new SqlCommand("""
                UPDATE billing.TenantSubscriptions
                SET CurrentPeriodEnd=DATEADD(day,-11,@Now)
                WHERE TenantId=@TenantId;
                """, connection, transaction))
            {
                expire.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                expire.Parameters.AddWithValue("@Now", now);
                await expire.ExecuteNonQueryAsync();
            }
            Assert.True(await IsBlockedAsync());
            var inactive = await ReserveAsync(Guid.NewGuid(), now);
            Assert.False(inactive.Reserved);
            Assert.Equal("SubscriptionInactive", inactive.Reason);
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        async Task<(bool Reserved, string? Reason)> ReserveAsync(Guid documentId, DateTimeOffset at)
        {
            await using var reserve = new SqlCommand(
                "dbo.TenantDianDocumentQuotaReserve", connection, transaction)
            { CommandType = System.Data.CommandType.StoredProcedure };
            reserve.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            reserve.Parameters.AddWithValue("@DocumentId", documentId);
            reserve.Parameters.AddWithValue("@DocumentKind", "Invoice");
            reserve.Parameters.AddWithValue("@Now", at);
            var reserved = reserve.Parameters.Add("@Reserved", System.Data.SqlDbType.Bit);
            reserved.Direction = System.Data.ParameterDirection.Output;
            var reason = reserve.Parameters.Add("@FailureReason", System.Data.SqlDbType.NVarChar, 32);
            reason.Direction = System.Data.ParameterDirection.Output;
            await reserve.ExecuteNonQueryAsync();
            return (reserved.Value is true, reason.Value as string);
        }

        async Task<bool> IsBlockedAsync()
        {
            await using var check = new SqlCommand(
                "dbo.TenantSubscriptionSuspensionGet", connection, transaction)
            { CommandType = System.Data.CommandType.StoredProcedure };
            check.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            return Convert.ToBoolean(await check.ExecuteScalarAsync());
        }
    }
}
