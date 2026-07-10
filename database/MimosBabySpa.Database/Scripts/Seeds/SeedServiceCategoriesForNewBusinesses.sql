-- ============================================================

-- Script: SeedServiceCategoriesForNewBusinesses

-- Inserta categorias (Planes Baby Spa, Taller, Clase) para negocios que aun no tengan.

-- Opcional: crea adjunto indicaciones para uso en messageSequences del agente (Agents.SettingsJson).

-- ============================================================



SET NOCOUNT ON;



DECLARE @BusinessId UNIQUEIDENTIFIER;

DECLARE @PlanCatId UNIQUEIDENTIFIER;



DECLARE biz_cursor CURSOR FOR

    SELECT b.BusinessId FROM dbo.Businesses b

    WHERE NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories sc WHERE sc.BusinessId = b.BusinessId);



OPEN biz_cursor;

FETCH NEXT FROM biz_cursor INTO @BusinessId;



WHILE @@FETCH_STATUS = 0

BEGIN

    IF NOT EXISTS (SELECT 1 FROM dbo.BusinessAttachments WHERE BusinessId = @BusinessId AND BlobPath = N'confirmations/indicaciones-para-tu-visita.pdf')

    BEGIN

        INSERT INTO dbo.BusinessAttachments (BusinessAttachmentId, BusinessId, BlobPath, MediaType, Filename, Description, IsActive, CreatedAt)

        VALUES (NEWID(), @BusinessId, N'confirmations/indicaciones-para-tu-visita.pdf', N'document', N'Indicaciones-para-tu-visita.pdf', N'Indicaciones para la visita', 1, GETUTCDATE());

    END



    SET @PlanCatId = NEWID();

    INSERT INTO dbo.ServiceCategories (ServiceCategoryId, BusinessId, Name, Description, DisplayOrder, IsActive, CreatedAt)

    VALUES

        (@PlanCatId, @BusinessId, N'Planes Baby Spa', N'Experiencias completas de spa para bebes: hidroterapia, masajes y momentos de relajacion o estimulacion segun la edad. Ideal cuando la familia quiere una vivencia principal y personalizada para bienestar, descanso y desarrollo sensorial.', 0, 1, GETUTCDATE()),

        (NEWID(), @BusinessId, N'Taller', N'Encuentros guiados por profesionales para trabajar temas puntuales del desarrollo y el cuidado del bebe, como estimulacion, juego, vinculo, rutinas o preparacion para nuevas etapas.', 1, 1, GETUTCDATE()),

        (NEWID(), @BusinessId, N'Clase', N'Espacios practicos y acompanados para que mama, papa o cuidadores aprendan tecnicas y actividades que pueden repetir en casa, compartiendo con el bebe de forma tranquila y segura.', 2, 1, GETUTCDATE());

    UPDATE dbo.ServiceCategories
    SET Description = CASE Name
        WHEN N'Planes Baby Spa' THEN N'Experiencias completas de spa para bebes: hidroterapia, masajes y momentos de relajacion o estimulacion segun la edad. Ideal cuando la familia quiere una vivencia principal y personalizada para bienestar, descanso y desarrollo sensorial.'
        WHEN N'Taller' THEN N'Encuentros guiados por profesionales para trabajar temas puntuales del desarrollo y el cuidado del bebe, como estimulacion, juego, vinculo, rutinas o preparacion para nuevas etapas.'
        WHEN N'Clase' THEN N'Espacios practicos y acompanados para que mama, papa o cuidadores aprendan tecnicas y actividades que pueden repetir en casa, compartiendo con el bebe de forma tranquila y segura.'
        ELSE Description
    END,
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId
      AND Name IN (N'Planes Baby Spa', N'Taller', N'Clase')
      AND (Description IS NULL OR LTRIM(RTRIM(Description)) = N'');



    FETCH NEXT FROM biz_cursor INTO @BusinessId;

END



CLOSE biz_cursor;

DEALLOCATE biz_cursor;



-- Renombrar la categoria historica "Plan" al nombre comercial actual.
UPDATE sc
SET Name = N'Planes Baby Spa',
    UpdatedAt = GETUTCDATE()
FROM dbo.ServiceCategories sc
WHERE sc.Name = N'Plan'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.ServiceCategories existing
      WHERE existing.BusinessId = sc.BusinessId
        AND existing.Name = N'Planes Baby Spa'
  );

UPDATE s
SET s.CategoryId = target.ServiceCategoryId,
    s.UpdatedAt = GETUTCDATE()
FROM dbo.Services s
INNER JOIN dbo.ServiceCategories source ON source.ServiceCategoryId = s.CategoryId
INNER JOIN dbo.ServiceCategories target
    ON target.BusinessId = source.BusinessId
   AND target.Name = N'Planes Baby Spa'
WHERE source.Name = N'Plan';

DELETE source
FROM dbo.ServiceCategories source
WHERE source.Name = N'Plan'
  AND NOT EXISTS (SELECT 1 FROM dbo.Services s WHERE s.CategoryId = source.ServiceCategoryId);
-- Enriquecer categorias existentes que fueron creadas antes de este seed.
UPDATE dbo.ServiceCategories
SET Description = CASE Name
    WHEN N'Planes Baby Spa' THEN N'Experiencias completas de spa para bebes: hidroterapia, masajes y momentos de relajacion o estimulacion segun la edad. Ideal cuando la familia quiere una vivencia principal y personalizada para bienestar, descanso y desarrollo sensorial.'
    WHEN N'Taller' THEN N'Encuentros guiados por profesionales para trabajar temas puntuales del desarrollo y el cuidado del bebe, como estimulacion, juego, vinculo, rutinas o preparacion para nuevas etapas.'
    WHEN N'Clase' THEN N'Espacios practicos y acompanados para que mama, papa o cuidadores aprendan tecnicas y actividades que pueden repetir en casa, compartiendo con el bebe de forma tranquila y segura.'
    ELSE Description
END,
    UpdatedAt = GETUTCDATE()
WHERE Name IN (N'Planes Baby Spa', N'Taller', N'Clase')
  AND (Description IS NULL OR LTRIM(RTRIM(Description)) = N'');

-- Eliminar la categoria generica "Otros": los servicios quedan sin categoria.
UPDATE s
SET s.CategoryId = NULL,
    s.UpdatedAt = GETUTCDATE()
FROM dbo.Services s
INNER JOIN dbo.ServiceCategories c ON c.ServiceCategoryId = s.CategoryId
WHERE c.Name = N'Otros';

DELETE c
FROM dbo.ServiceCategories c
WHERE c.Name = N'Otros'
  AND NOT EXISTS (SELECT 1 FROM dbo.Services s WHERE s.CategoryId = c.ServiceCategoryId);

-- Corregir Services con CategoryId huerfano: dejar sin categoria.
UPDATE s
SET s.CategoryId = NULL,
    s.UpdatedAt = GETUTCDATE()
FROM dbo.Services s
WHERE s.CategoryId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories c WHERE c.ServiceCategoryId = s.CategoryId);

PRINT N'Seed de categorias completado.';

GO
