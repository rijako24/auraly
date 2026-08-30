SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Tenants', N'TenantKey') IS NOT NULL
   AND COL_LENGTH(N'dbo.Tenants', N'UpdatedAt') IS NOT NULL
    EXEC sys.sp_executesql N'
        DECLARE @AuralyTenantId UNIQUEIDENTIFIER = ''A0A10000-0000-0000-0000-000000000000'';
        IF EXISTS (
            SELECT 1 FROM dbo.Tenants
            WHERE TenantKey=N''@auraly'' AND TenantId<>@AuralyTenantId)
            THROW 51000, ''El tenant key @auraly ya pertenece a otro tenant.'', 1;
        UPDATE dbo.Tenants
        SET TenantKey=N''@auraly'',UpdatedAt=SYSUTCDATETIME()
        WHERE TenantId=@AuralyTenantId AND TenantKey<>N''@auraly'';';
ELSE
    PRINT N'NormalizeAuralyPlatformTenantKey: target columns do not exist yet; skipped before DACPAC creation.';
GO
