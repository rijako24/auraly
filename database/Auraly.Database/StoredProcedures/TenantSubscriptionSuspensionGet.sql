CREATE PROCEDURE dbo.TenantSubscriptionSuspensionGet
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(bit,CASE WHEN EXISTS(
        SELECT 1 FROM dbo.Tenants
        WHERE TenantId=@TenantId AND IsActive=0)
        OR EXISTS(
        SELECT 1 FROM billing.TenantSubscriptions subscription
        CROSS JOIN billing.PlatformBillingSettings settings
        WHERE subscription.TenantId=@TenantId
          AND settings.PlatformBillingSettingId=1
          AND (subscription.Status IN(N'Suspended',N'Cancelled')
            OR SYSDATETIMEOFFSET()>=DATEADD(day,settings.GracePeriodDays,
                                               subscription.CurrentPeriodEnd)))
        THEN 1 ELSE 0 END);
END;
GO
