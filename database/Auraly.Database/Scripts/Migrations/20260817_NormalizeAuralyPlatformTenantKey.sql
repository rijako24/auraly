SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Tenants', N'TenantKey') IS NULL
        ALTER TABLE dbo.Tenants ADD TenantKey NVARCHAR(64) NULL;
    IF COL_LENGTH(N'dbo.Tenants', N'UpdatedAt') IS NULL
        ALTER TABLE dbo.Tenants ADD UpdatedAt DATETIME2 NULL;

    EXEC sys.sp_executesql N'
        UPDATE dbo.Tenants
        SET TenantKey=CONCAT(N''@tenant-'',LOWER(REPLACE(CONVERT(NVARCHAR(36),NEWID()),N''-'',N'''')))
        WHERE TenantKey IS NULL OR LEN(LTRIM(RTRIM(TenantKey)))<3;

        DECLARE @AuralyTenantId UNIQUEIDENTIFIER = ''A0A10000-0000-0000-0000-000000000000'';
        IF EXISTS (
            SELECT 1 FROM dbo.Tenants
            WHERE TenantKey=N''@auraly'' AND TenantId<>@AuralyTenantId)
            THROW 51000, ''El tenant key @auraly ya pertenece a otro tenant.'', 1;
        UPDATE dbo.Tenants
        SET TenantKey=N''@auraly'',UpdatedAt=SYSUTCDATETIME()
        WHERE TenantId=@AuralyTenantId AND TenantKey<>N''@auraly'';';

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id=OBJECT_ID(N'dbo.Tenants')
          AND name=N'TenantKey' AND is_nullable=1)
        ALTER TABLE dbo.Tenants ALTER COLUMN TenantKey NVARCHAR(64) NOT NULL;
END
ELSE
    PRINT N'NormalizeAuralyPlatformTenantKey: fresh database; skipped before schema creation.';
GO
