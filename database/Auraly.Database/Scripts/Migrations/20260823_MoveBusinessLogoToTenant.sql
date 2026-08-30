SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TenantLegalProfiles', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.TenantLegalProfiles', N'LogoMediaRef') IS NULL
BEGIN
    ALTER TABLE dbo.TenantLegalProfiles
        ADD LogoMediaRef NVARCHAR(500) NULL;
END;

IF COL_LENGTH(N'dbo.Businesses', N'LogoUrl') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.TenantLegalProfiles', N'U') IS NOT NULL
    BEGIN
        -- SQL Server compila el lote completo antes de ejecutar el ALTER anterior.
        -- El SQL dinamico permite usar LogoMediaRef tambien en bases donde aun no existia.
        EXEC sys.sp_executesql N'
            UPDATE profile
            SET LogoMediaRef = business.LogoUrl
            FROM dbo.TenantLegalProfiles profile
            INNER JOIN dbo.Businesses business
                ON business.BusinessId = profile.PrimaryBusinessId
            WHERE NULLIF(LTRIM(RTRIM(profile.LogoMediaRef)), N'''') IS NULL
              AND NULLIF(LTRIM(RTRIM(business.LogoUrl)), N'''') IS NOT NULL;';
    END
    ELSE IF EXISTS (
        SELECT 1 FROM dbo.Businesses
        WHERE NULLIF(LTRIM(RTRIM(LogoUrl)), N'') IS NOT NULL)
    BEGIN
        THROW 51091, 'No se pueden preservar logos: TenantLegalProfiles aun no existe.', 1;
    END;

    ALTER TABLE dbo.Businesses DROP COLUMN LogoUrl;
END;

PRINT 'MoveBusinessLogoToTenant: logos existentes preservados en el perfil legal del tenant.';
