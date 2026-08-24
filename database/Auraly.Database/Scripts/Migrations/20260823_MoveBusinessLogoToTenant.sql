SET NOCOUNT ON;

IF COL_LENGTH(N'dbo.TenantLegalProfiles', N'LogoMediaRef') IS NULL
BEGIN
    ALTER TABLE dbo.TenantLegalProfiles
        ADD LogoMediaRef NVARCHAR(500) NULL;
END;

IF COL_LENGTH(N'dbo.Businesses', N'LogoUrl') IS NOT NULL
BEGIN
    UPDATE profile
    SET LogoMediaRef = business.LogoUrl
    FROM dbo.TenantLegalProfiles profile
    INNER JOIN dbo.Businesses business
        ON business.BusinessId = profile.PrimaryBusinessId
    WHERE NULLIF(LTRIM(RTRIM(profile.LogoMediaRef)), N'') IS NULL
      AND NULLIF(LTRIM(RTRIM(business.LogoUrl)), N'') IS NOT NULL;

    ALTER TABLE dbo.Businesses DROP COLUMN LogoUrl;
END;

PRINT 'MoveBusinessLogoToTenant: logos existentes preservados en el perfil legal del tenant.';
