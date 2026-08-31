SET NOCOUNT ON;

DECLARE @AuralyBillingAccountingTenantId UNIQUEIDENTIFIER='A0A10000-0000-0000-0000-000000000000';
DECLARE @AuralyBillingAccountingBusinessId UNIQUEIDENTIFIER='A0A10000-0000-0000-0000-000000000001';
DECLARE @AuralyBillingAccountingNow DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();

IF EXISTS(
    SELECT 1 FROM dbo.Businesses businessValue
    JOIN dbo.Tenants tenantValue ON tenantValue.TenantId=businessValue.TenantId
    WHERE businessValue.BusinessId=@AuralyBillingAccountingBusinessId
      AND tenantValue.TenantId=@AuralyBillingAccountingTenantId AND tenantValue.TenantKey=N'@auraly'
      AND businessValue.IsActive=1)
BEGIN
    EXEC dbo.AccountingDefaultsProvision
        @TenantId=@AuralyBillingAccountingTenantId,
        @BusinessId=@AuralyBillingAccountingBusinessId,@Now=@AuralyBillingAccountingNow;
    PRINT N'SeedAuralyBillingAccounting: contabilidad de la empresa facturadora preparada.';
END;
GO
