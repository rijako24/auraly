-- Selection is independent of live account readiness. Keep the winning site
-- override even when its account needs correction; never select a shared fallback.
CREATE FUNCTION accounting.ConfiguredPostingAccountIdentity(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@Category nvarchar(128),@OccurredOn date)
RETURNS TABLE
AS RETURN
(
    SELECT TOP(1) mapping.AccountId
    FROM dbo.AccountingAccountMappings mapping
    WHERE mapping.TenantId=@TenantId AND mapping.Category=@Category
      AND (mapping.BusinessId=@BusinessId OR mapping.BusinessId IS NULL)
      AND mapping.EffectiveFrom<=@OccurredOn AND (mapping.EffectiveTo IS NULL OR mapping.EffectiveTo>=@OccurredOn)
    ORDER BY CASE WHEN mapping.BusinessId=@BusinessId THEN 0 ELSE 1 END,mapping.EffectiveFrom DESC
);
GO
