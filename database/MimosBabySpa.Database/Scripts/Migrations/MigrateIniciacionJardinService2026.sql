-- =============================================================================
-- MigrateIniciacionJardinService2026.sql
--
-- Crea/actualiza el Programa de Iniciacion al Jardin en el catalogo de
-- Mimos Baby Spa como servicio de inscripcion con horario fijo.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @ServiceName NVARCHAR(200) = N'Programa de Iniciaci' + NCHAR(243) + N'n al Jard' + NCHAR(237) + N'n';
DECLARE @CategoryName NVARCHAR(100) = N'Iniciaci' + NCHAR(243) + N'n al Jard' + NCHAR(237) + N'n';
DECLARE @LegacyCategoryName NVARCHAR(100) = N'Programa';
DECLARE @Description NVARCHAR(MAX) = N'Espacio para acompanar la transicion a la etapa escolar, fortaleciendo autonomia, habilidades sociales, rutinas, desarrollo emocional y preparacion para el jardin en un ambiente calido y guiado por profesionales. Inversion: mensualidad $380.000 COP; inscripcion de pago unico $100.000 COP; uniforme con valor pendiente por definir.';
DECLARE @FixedScheduleLabel NVARCHAR(500) = N'lunes a viernes 08:00-11:30';
DECLARE @CategoryDescription NVARCHAR(MAX) = N'Programa de acompanamiento infantil con inscripcion y horario fijo para preparar la transicion al jardin. Fortalece autonomia, socializacion, rutinas, lenguaje, motricidad y seguridad emocional en un ambiente calido y guiado.';
DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

DECLARE @Businesses TABLE
(
    BusinessId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
);

INSERT INTO @Businesses (BusinessId)
SELECT b.BusinessId
FROM dbo.Businesses b
WHERE b.BusinessId = @MimosBusinessId
  AND b.IsActive = 1;

UPDATE dbo.Services
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId <> @MimosBusinessId
  AND (
        ServiceName = @ServiceName
        OR ServiceName LIKE N'Programa de Iniciaci%n al Jard%n'
      );

DECLARE @BusinessId UNIQUEIDENTIFIER;
DECLARE @CategoryId UNIQUEIDENTIFIER;
DECLARE @ServiceId UNIQUEIDENTIFIER;

DECLARE business_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT BusinessId FROM @Businesses;

OPEN business_cursor;
FETCH NEXT FROM business_cursor INTO @BusinessId;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @CategoryId = sc.ServiceCategoryId
    FROM dbo.ServiceCategories sc
    WHERE sc.BusinessId = @BusinessId
      AND sc.Name = @CategoryName;

    IF @CategoryId IS NULL
    BEGIN
        SELECT @CategoryId = sc.ServiceCategoryId
        FROM dbo.ServiceCategories sc
        WHERE sc.BusinessId = @BusinessId
          AND sc.Name = @LegacyCategoryName;
    END
    IF @CategoryId IS NULL
    BEGIN
        SET @CategoryId = NEWID();

        INSERT INTO dbo.ServiceCategories
            (ServiceCategoryId, BusinessId, Name, Description, DisplayOrder, IsActive, CreatedAt)
        VALUES
            (@CategoryId, @BusinessId, @CategoryName,
             @CategoryDescription,
             3, 1, GETUTCDATE());
    END
    ELSE
    BEGIN
        UPDATE dbo.ServiceCategories
        SET Name = @CategoryName,
            Description = CASE WHEN Description IS NULL OR LTRIM(RTRIM(Description)) = N'' OR Description = N'Programa de acompanamiento infantil con inscripcion y horario fijo.' THEN @CategoryDescription ELSE Description END,
            DisplayOrder = CASE WHEN DisplayOrder = 99 THEN 3 ELSE DisplayOrder END,
            IsActive = 1,
            UpdatedAt = GETUTCDATE()
        WHERE ServiceCategoryId = @CategoryId;
    END

    SELECT TOP 1 @ServiceId = s.ServiceId
    FROM dbo.Services s
    WHERE s.BusinessId = @BusinessId
      AND (
            s.ServiceName = @ServiceName
            OR s.ServiceName LIKE N'Programa de Iniciaci%n al Jard%n'
          )
    ORDER BY
        CASE WHEN s.ServiceName = @ServiceName THEN 0 ELSE 1 END,
        s.CreatedAt,
        s.ServiceId;

    IF @ServiceId IS NOT NULL
    BEGIN
        UPDATE dbo.Services
        SET ServiceName = @ServiceName,
            Description = @Description,
            DurationMinutes = 210,
            Price = 380000,
            IncludeInCheckoutTotal = 1,
            CategoryId = @CategoryId,
            Tier = 0,
            ServiceType = 0,
            FulfillmentKind = 1,
            FixedScheduleLabel = @FixedScheduleLabel,
            IsActive = 1,
            UpdatedAt = GETUTCDATE()
        WHERE ServiceId = @ServiceId;

        UPDATE dbo.Services
        SET IsActive = 0,
            UpdatedAt = GETUTCDATE()
        WHERE BusinessId = @BusinessId
          AND (
                ServiceName = @ServiceName
                OR ServiceName LIKE N'Programa de Iniciaci%n al Jard%n'
              )
          AND ServiceId <> @ServiceId;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.Services
            (ServiceId, BusinessId, ServiceName, Description, DurationMinutes, Price,
             IncludeInCheckoutTotal, CategoryId, Tier, ServiceType, FulfillmentKind,
             FixedScheduleLabel, IsActive, CreatedAt)
        VALUES
            (NEWID(), @BusinessId, @ServiceName, @Description, 210, 380000,
             1, @CategoryId, 0, 0, 1, @FixedScheduleLabel, 1, GETUTCDATE());
    END

    SET @CategoryId = NULL;
    SET @ServiceId = NULL;
    FETCH NEXT FROM business_cursor INTO @BusinessId;
END

CLOSE business_cursor;
DEALLOCATE business_cursor;

PRINT N'MigrateIniciacionJardinService2026: ' + @ServiceName + N' configurado.';
GO
