SET NOCOUNT ON;

DECLARE @PlatformTenantId UNIQUEIDENTIFIER='A0A10000-0000-0000-0000-000000000000';
DECLARE @BillingBusinessId UNIQUEIDENTIFIER='A0A10000-0000-0000-0000-000000000001';
DECLARE @Now DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();

IF NOT EXISTS(
    SELECT 1 FROM dbo.Businesses businessValue
    JOIN dbo.Tenants tenantValue ON tenantValue.TenantId=businessValue.TenantId
    WHERE businessValue.BusinessId=@BillingBusinessId
      AND tenantValue.TenantId=@PlatformTenantId AND tenantValue.TenantKey=N'@auraly'
      AND businessValue.IsActive=1)
    THROW 51000,'SeedAuralyBillingConfiguration requiere la empresa canónica Auraly.',1;

IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles
              WHERE BusinessId=@BillingBusinessId AND DianTaxCode=N'01' AND Rate=19)
    INSERT dbo.TaxProfiles
      (TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
    VALUES('A0A1B100-0000-0000-0000-000000000019',@BillingBusinessId,N'IVA19',N'01',
           N'IVA 19%',19,1,@Now);

PRINT N'SeedAuralyBillingConfiguration: identidad fiscal de Auraly preparada.';
GO
