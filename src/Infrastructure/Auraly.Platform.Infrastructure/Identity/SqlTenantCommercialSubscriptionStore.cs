using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantCommercialSubscriptionStore(ApplicationDbContext db)
    : ITenantCommercialSubscriptionStore
{
    public async Task<TenantCommercialSubscriptionDto?> GetAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT subscription.TenantSubscriptionId,serviceValue.Code,serviceValue.Name,
                   subscription.BillingPeriod,subscription.Status,
                   subscription.CurrentPeriodStart,subscription.CurrentPeriodEnd,
                   subscription.FullUserLimit,subscription.SellerUserLimit,
                   subscription.PosDeviceLimit,subscription.DianDocumentMonthlyLimit,
                   COALESCE(periodValue.DianDocumentsUsed,0),subscription.PayrollEmployeeLimit
            FROM billing.TenantSubscriptions subscription
            INNER JOIN billing.TenantCommercialPlans planValue
              ON planValue.TenantCommercialPlanId=subscription.TenantCommercialPlanId
            INNER JOIN billing.BillableServices serviceValue
              ON serviceValue.BillableServiceId=planValue.BillableServiceId
            OUTER APPLY(
              SELECT TOP(1) usageValue.DianDocumentsUsed
              FROM billing.TenantSubscriptionUsagePeriods usageValue
              WHERE usageValue.TenantSubscriptionId=subscription.TenantSubscriptionId
                AND SYSUTCDATETIME()>=usageValue.PeriodStart
                AND SYSUTCDATETIME()<usageValue.PeriodEnd
              ORDER BY usageValue.PeriodStart DESC) periodValue
            WHERE subscription.TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6), reader.GetInt32(7), reader.GetInt32(8),
            reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12));
    }

    public async Task<PlatformTenantSubscriptionPageDto> ListPlatformAsync(
        int page, int pageSize, string? search, string? status,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(*)
            FROM dbo.Tenants tenantValue
            LEFT JOIN billing.TenantSubscriptions subscription
              ON subscription.TenantId=tenantValue.TenantId
            WHERE (@Search IS NULL OR tenantValue.Name LIKE N'%'+@Search+N'%'
                   OR tenantValue.TenantKey LIKE N'%'+@Search+N'%'
                   OR tenantValue.Email LIKE N'%'+@Search+N'%')
              AND (@Status IS NULL
                   OR (@Status=N'Missing' AND subscription.TenantSubscriptionId IS NULL)
                   OR subscription.Status=@Status);

            SELECT tenantValue.TenantId,tenantValue.TenantKey,tenantValue.Name,tenantValue.Email,
                   subscription.TenantSubscriptionId,serviceValue.Code,serviceValue.Name,
                   subscription.BillingPeriod,subscription.Status,
                   subscription.CurrentPeriodStart,subscription.CurrentPeriodEnd,
                   subscription.FullUserLimit,subscription.SellerUserLimit,
                   subscription.PosDeviceLimit,subscription.DianDocumentMonthlyLimit,
                   COALESCE(periodValue.DianDocumentsUsed,0),subscription.PayrollEmployeeLimit,
                   renewal.TenantSubscriptionRenewalOrderId,renewal.Status,renewal.DueAt,
                   renewal.PayableAmount
            FROM dbo.Tenants tenantValue
            LEFT JOIN billing.TenantSubscriptions subscription
              ON subscription.TenantId=tenantValue.TenantId
            LEFT JOIN billing.TenantCommercialPlans planValue
              ON planValue.TenantCommercialPlanId=subscription.TenantCommercialPlanId
            LEFT JOIN billing.BillableServices serviceValue
              ON serviceValue.BillableServiceId=planValue.BillableServiceId
            OUTER APPLY(
              SELECT TOP(1) usageValue.DianDocumentsUsed
              FROM billing.TenantSubscriptionUsagePeriods usageValue
              WHERE usageValue.TenantSubscriptionId=subscription.TenantSubscriptionId
                AND SYSUTCDATETIME()>=usageValue.PeriodStart
                AND SYSUTCDATETIME()<usageValue.PeriodEnd
              ORDER BY usageValue.PeriodStart DESC) periodValue
            OUTER APPLY(
              SELECT TOP(1) orderValue.TenantSubscriptionRenewalOrderId,
                     orderValue.Status,orderValue.DueAt,orderValue.PayableAmount
              FROM billing.TenantSubscriptionRenewalOrders orderValue
              WHERE orderValue.TenantSubscriptionId=subscription.TenantSubscriptionId
                AND orderValue.IsCurrent=1
              ORDER BY orderValue.TargetPeriodStart DESC,orderValue.Revision DESC) renewal
            WHERE (@Search IS NULL OR tenantValue.Name LIKE N'%'+@Search+N'%'
                   OR tenantValue.TenantKey LIKE N'%'+@Search+N'%'
                   OR tenantValue.Email LIKE N'%'+@Search+N'%')
              AND (@Status IS NULL
                   OR (@Status=N'Missing' AND subscription.TenantSubscriptionId IS NULL)
                   OR subscription.Status=@Status)
            ORDER BY CASE
                       WHEN subscription.TenantSubscriptionId IS NULL THEN 0
                       WHEN subscription.Status=N'Suspended' THEN 1
                       WHEN subscription.Status=N'PastDue' THEN 2 ELSE 3 END,
                     subscription.CurrentPeriodEnd,tenantValue.Name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = checked((int)reader.GetInt64(0));
        await reader.NextResultAsync(cancellationToken);
        var items = new List<PlatformTenantSubscriptionDto>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
                reader.IsDBNull(14) ? null : reader.GetInt32(14),
                reader.IsDBNull(15) ? null : reader.GetInt32(15),
                reader.IsDBNull(16) ? null : reader.GetInt32(16),
                reader.IsDBNull(17) ? null : reader.GetGuid(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19),
                reader.IsDBNull(20) ? null : reader.GetDecimal(20)));
        return new(items, total, page, pageSize);
    }
}
