-- =============================================================================
-- Migration 019: Dynamic Catalog & Scheduling Policy
--
-- Changes:
--   1. Add Description column to ServiceCategories table (nullable NVARCHAR(500)).
--   2. Seed category descriptions for Mimos Baby Spa.
--   3. Seed SchedulingPolicy (key=9) for Mimos Baby Spa business.
--
-- After this migration:
--   - ServiceCatalogBuilder renders the category description as an introductory
--     line beneath each category heading.
--   - AvailabilityService reads slotIntervalMinutes, bufferBetweenAppointmentsMinutes,
--     etc. from the DB instead of using a hardcoded constant of 60.
--   - FlowOrchestrationService dynamically replaces ServiceCatalog KnowledgeSources
--     content with output from CatalogContentGenerator — no more static text to maintain.
-- =============================================================================

BEGIN TRANSACTION;

-- ============================================================
-- 1. Add Description column to ServiceCategories
-- ============================================================
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME   = 'ServiceCategories'
      AND COLUMN_NAME  = 'Description'
)
BEGIN
    ALTER TABLE [dbo].[ServiceCategories]
        ADD [Description] NVARCHAR(500) NULL;
    PRINT 'Column Description added to ServiceCategories.';
END
ELSE
    PRINT 'Column Description already exists in ServiceCategories — skipping.';

-- ============================================================
-- 2. Seed category descriptions for Mimos Baby Spa
-- ============================================================
DECLARE @BusinessId UNIQUEIDENTIFIER;
SELECT TOP 1 @BusinessId = BusinessId
FROM [dbo].[Businesses]
WHERE Name = 'Mimos Baby Spa';

IF @BusinessId IS NULL
BEGIN
    RAISERROR('Business "Mimos Baby Spa" not found. Aborting.', 16, 1);
    ROLLBACK TRANSACTION; RETURN;
END

-- Update each category by name (idempotent — only sets if NULL or empty)
UPDATE [dbo].[ServiceCategories]
SET    [Description]  = N'Sesiones de bienestar diseñadas para relajar, estimular y fortalecer el vínculo con tu bebé, en un entorno seguro y amoroso.',
       [UpdatedAt]    = GETUTCDATE()
WHERE  [BusinessId]   = @BusinessId
  AND  [Name]         = N'Planes Baby Spa'
  AND  (Description   IS NULL OR Description = N'');

UPDATE [dbo].[ServiceCategories]
SET    [Description]  = N'Talleres grupales y personalizados de estimulación temprana para potenciar el desarrollo motor, sensorial y cognitivo de tu bebé.',
       [UpdatedAt]    = GETUTCDATE()
WHERE  [BusinessId]   = @BusinessId
  AND  [Name]         = N'Talleres de Estimulación Temprana'
  AND  (Description   IS NULL OR Description = N'');

UPDATE [dbo].[ServiceCategories]
SET    [Description]  = N'Experiencias de bienestar para mamás en gestación o recién paridas, combinando relajación y cuidado prenatal/postnatal.',
       [UpdatedAt]    = GETUTCDATE()
WHERE  [BusinessId]   = @BusinessId
  AND  [Name]         = N'Materno Spa'
  AND  (Description   IS NULL OR Description = N'');

UPDATE [dbo].[ServiceCategories]
SET    [Description]  = N'Programa de acompañamiento y bienestar para la etapa del embarazo, enfocado en la conexión temprana con el bebé.',
       [UpdatedAt]    = GETUTCDATE()
WHERE  [BusinessId]   = @BusinessId
  AND  [Name]         = N'Dulce Espera'
  AND  (Description   IS NULL OR Description = N'');

UPDATE [dbo].[ServiceCategories]
SET    [Description]  = N'Programa de transición lúdica y estimulante para bebés que se preparan para integrarse al jardín infantil.',
       [UpdatedAt]    = GETUTCDATE()
WHERE  [BusinessId]   = @BusinessId
  AND  [Name]         = N'Programa Iniciación al Jardín'
  AND  (Description   IS NULL OR Description = N'');

PRINT 'Category descriptions seeded for Mimos Baby Spa.';

-- ============================================================
-- 3. Seed SchedulingPolicy (BusinessConfigurationKey = 9)
--    for Mimos Baby Spa
-- ============================================================
DECLARE @PolicyJson NVARCHAR(MAX) = N'{
  "slotIntervalMinutes": 60,
  "bufferBetweenAppointmentsMinutes": 0,
  "requireEmployee": true,
  "employeeStrategy": "least_versatile",
  "maxAdvanceBookingDays": 90,
  "minAdvanceBookingHours": 0
}';

IF NOT EXISTS (
    SELECT 1
    FROM [dbo].[BusinessConfigurations]
    WHERE [BusinessId] = @BusinessId
      AND [Key]        = 9  -- SchedulingPolicy
)
BEGIN
    INSERT INTO [dbo].[BusinessConfigurations] ([BusinessConfigurationId], [BusinessId], [Key], [Value], [CreatedAt], [UpdatedAt])
    VALUES (NEWID(), @BusinessId, 9, @PolicyJson, GETUTCDATE(), GETUTCDATE());
    PRINT 'SchedulingPolicy inserted for Mimos Baby Spa.';
END
ELSE
BEGIN
    UPDATE [dbo].[BusinessConfigurations]
    SET    [Value]     = @PolicyJson,
           [UpdatedAt] = GETUTCDATE()
    WHERE  [BusinessId] = @BusinessId
      AND  [Key]        = 9;
    PRINT 'SchedulingPolicy updated for Mimos Baby Spa.';
END

COMMIT TRANSACTION;
PRINT '019_DynamicCatalogAndSchedulingPolicy completed successfully.';
