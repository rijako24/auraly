SET NOCOUNT ON;

DECLARE @AuralyTenantId UNIQUEIDENTIFIER = 'A0A10000-0000-0000-0000-000000000000';

IF EXISTS (
    SELECT 1
    FROM dbo.Tenants
    WHERE TenantKey = N'@auraly'
      AND TenantId <> @AuralyTenantId)
BEGIN
    THROW 51000, 'El tenant key @auraly ya pertenece a otro tenant.', 1;
END;

UPDATE dbo.Tenants
SET TenantKey = N'@auraly',
    UpdatedAt = SYSUTCDATETIME()
WHERE TenantId = @AuralyTenantId
  AND TenantKey <> N'@auraly';

PRINT N'NormalizeAuralyPlatformTenantKey: tenant canónico verificado.';
GO