SET NOCOUNT ON;

DECLARE @BillingBusinessId UNIQUEIDENTIFIER = (
    SELECT TOP(1) businessValue.BusinessId
    FROM dbo.Businesses businessValue
    JOIN dbo.Tenants tenantValue ON tenantValue.TenantId=businessValue.TenantId
    WHERE tenantValue.TenantKey=N'@auraly' AND businessValue.IsActive=1
      AND businessValue.BusinessId='A0A10000-0000-0000-0000-000000000001'
    ORDER BY businessValue.CreatedAt,businessValue.BusinessId);
DECLARE @AdministratorUserId UNIQUEIDENTIFIER = (
    SELECT TOP(1) userValue.UserId
    FROM dbo.AppUsers userValue
    JOIN dbo.Tenants tenantValue ON tenantValue.TenantId=userValue.TenantId
    WHERE tenantValue.TenantKey=N'@auraly' AND userValue.IsActive=1
    ORDER BY CASE WHEN userValue.NormalizedUsername=N'ADMIN' THEN 0 ELSE 1 END,userValue.CreatedAt);

IF @BillingBusinessId IS NOT NULL AND @AdministratorUserId IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM billing.PlatformBillingSettings WHERE PlatformBillingSettingId=1)
        UPDATE billing.PlatformBillingSettings
        SET BillingBusinessId=@BillingBusinessId,UpdatedByUserId=@AdministratorUserId,
            UpdatedAt=SYSDATETIMEOFFSET()
        WHERE PlatformBillingSettingId=1;
    ELSE
        INSERT billing.PlatformBillingSettings
          (PlatformBillingSettingId,BillingBusinessId,EmailRemindersEnabled,PreDueReminderDays,
           OverdueReminderIntervalDays,GracePeriodDays,BillingTimeZoneId,UpdatedByUserId,UpdatedAt)
        VALUES(1,@BillingBusinessId,1,5,3,10,N'America/Bogota',@AdministratorUserId,SYSDATETIMEOFFSET());
END;
GO
