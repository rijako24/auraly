-- =============================================================================

-- SeedSolorzanoBusinessIdentity.sql

--

-- Normaliza la identidad visible de Vinos Artesanales Solorzano.

-- Usa NCHAR para conservar la tilde aun si el archivo se abre con otra

-- codificacion en alguna terminal o editor.

-- =============================================================================



SET NOCOUNT ON;

IF LOWER(N'$(DeploymentEnvironment)') = N'prod'
BEGIN
    PRINT N'SeedSolorzanoBusinessIdentity: seed de demostración omitido en producción.';
    RETURN;
END;



DECLARE @SolorzanoTenantId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB7246800';

DECLARE @SolorzanoBusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';

DECLARE @SolorzanoName NVARCHAR(200) = N'Vinos Artesanales Sol' + NCHAR(243) + N'rzano';

DECLARE @SolorzanoDescription NVARCHAR(2000) =

    N'Productores de vinos artesanales elaborados con fruta seleccionada de la region.';

DECLARE @EffectiveTenantId UNIQUEIDENTIFIER;



SELECT @EffectiveTenantId = TenantId

FROM dbo.Businesses

WHERE BusinessId = @SolorzanoBusinessId;



SET @EffectiveTenantId = COALESCE(@EffectiveTenantId, @SolorzanoTenantId);



IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @EffectiveTenantId)

BEGIN

    INSERT INTO dbo.Tenants (TenantId, Name, Email, IsActive, CreatedAt)

    VALUES (@EffectiveTenantId, @SolorzanoName, N'admin@vinosartesanales-solorzano.com', 1, GETUTCDATE());

END

ELSE

BEGIN

    UPDATE dbo.Tenants

    SET Name = @SolorzanoName,

        Email = COALESCE(NULLIF(Email, N''), N'admin@vinosartesanales-solorzano.com'),

        IsActive = 1,

        UpdatedAt = GETUTCDATE()

    WHERE TenantId = @EffectiveTenantId;

END



IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @SolorzanoBusinessId)

BEGIN

    INSERT INTO dbo.Businesses

        (BusinessId, TenantId, Name, Description, Address, Phone, Email, Website, TimeZone, IsActive, CreatedAt)

    VALUES

        (@SolorzanoBusinessId, @EffectiveTenantId, @SolorzanoName, @SolorzanoDescription,

         N'Calle 16 # 9-35, Centro, Valledupar', N'+573004442469',

         N'admin@vinosartesanales-solorzano.com', N'', N'America/Bogota', 1, GETUTCDATE());

END

ELSE

BEGIN

    UPDATE dbo.Businesses

    SET Name = @SolorzanoName,

        Description = CASE

            WHEN Description IS NULL OR Description = N'' OR Description LIKE N'%Sol' + NCHAR(195) + N'%'

                THEN @SolorzanoDescription

            ELSE Description

        END,

        Address = COALESCE(NULLIF(Address, N''), N'Calle 16 # 9-35, Centro, Valledupar'),

        Phone = COALESCE(NULLIF(Phone, N''), N'+573004442469'),

        Email = COALESCE(NULLIF(Email, N''), N'admin@vinosartesanales-solorzano.com'),

        Website = COALESCE(Website, N''),

        TimeZone = N'America/Bogota',

        IsActive = 1,

        UpdatedAt = GETUTCDATE()

    WHERE BusinessId = @SolorzanoBusinessId;

END



PRINT N'SeedSolorzanoBusinessIdentity: identidad normalizada para ' + @SolorzanoName + N'.';

GO

