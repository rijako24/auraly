CREATE FUNCTION billing.TenantDianQuotaSnapshot
(
    @TenantId UNIQUEIDENTIFIER,
    @Now DATETIMEOFFSET(7)
)
RETURNS TABLE
AS RETURN
(
    SELECT subscription.TenantSubscriptionId,
           periodValue.TenantSubscriptionUsagePeriodId,
           COALESCE(subscription.DianDocumentMonthlyLimit,0) AS DocumentLimit,
           COALESCE(periodValue.DianDocumentsUsed,0) AS DocumentsUsed,
           CONVERT(bit,CASE WHEN tenant.IsActive=1 AND
             (subscription.TenantSubscriptionId IS NULL OR
              ((periodValue.TenantSubscriptionUsagePeriodId IS NOT NULL OR
                (@Now>=subscription.CurrentPeriodStart AND @Now<subscription.CurrentPeriodEnd))
               AND COALESCE(periodValue.DianDocumentsUsed,0)<subscription.DianDocumentMonthlyLimit))
             THEN 1 ELSE 0 END) AS HasQuota
    FROM dbo.Tenants tenant
    LEFT JOIN billing.TenantSubscriptions subscription ON subscription.TenantId=tenant.TenantId
    OUTER APPLY(
        SELECT TOP(1) usageValue.TenantSubscriptionUsagePeriodId,usageValue.DianDocumentsUsed
        FROM billing.TenantSubscriptionUsagePeriods usageValue
        WHERE usageValue.TenantSubscriptionId=subscription.TenantSubscriptionId
          AND usageValue.PeriodStart<=@Now AND usageValue.PeriodEnd>@Now
        ORDER BY usageValue.PeriodStart DESC
    ) periodValue
    WHERE tenant.TenantId=@TenantId
);
GO
