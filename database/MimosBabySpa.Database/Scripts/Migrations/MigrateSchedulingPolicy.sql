-- =============================================================================
-- MigrateSchedulingPolicy.sql  (ONE-SHOT — ejecutar una sola vez en BDs existentes)
--
-- Consolida configuración legacy de agendamiento en Key=2 (SchedulingPolicy):
--   - Key=9: slotInterval/buffer/requireEmployee (sin schedule)
--   - Key=4: OperatingHours (solo schedule por día)
--
-- Si ya existe Key=2, no sobrescribe el Value.
-- Elimina keys huérfanas 4 y 9 tras migrar.
--
-- NO incluir en PostDeployment.sql — bases nuevas usan SeedSchedulingPolicy.sql.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @DefaultSchedule NVARCHAR(MAX) = N'{
  "monday":    [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "tuesday":   [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "wednesday": [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "thursday":  [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "friday":    [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "saturday":  [{"open":"08:00","close":"13:00"}],
  "sunday":    []
}';

BEGIN TRANSACTION;

-- Crear Key=2 desde Key=9 + schedule de Key=4 (o default) cuando no existe Key=2
INSERT INTO dbo.BusinessConfigurations (
    BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
SELECT
    NEWID(),
    b.BusinessId,
    2,
    (
        SELECT
            COALESCE(JSON_VALUE(k9.[Value], '$.slotIntervalMinutes'), '60') AS slotIntervalMinutes,
            COALESCE(JSON_VALUE(k9.[Value], '$.bufferBetweenAppointmentsMinutes'), '0') AS bufferBetweenAppointmentsMinutes,
            COALESCE(JSON_VALUE(k9.[Value], '$.requireEmployee'), 'true') AS requireEmployee,
            COALESCE(JSON_VALUE(k9.[Value], '$.employeeStrategy'), 'least_versatile') AS employeeStrategy,
            JSON_QUERY(COALESCE(k4.[Value], @DefaultSchedule), '$') AS schedule
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ),
    N'Política de agendamiento (migrada desde keys legacy 4/9)',
    1,
    GETUTCDATE()
FROM dbo.Businesses b
LEFT JOIN dbo.BusinessConfigurations k9
    ON k9.BusinessId = b.BusinessId AND k9.[Key] = 9
LEFT JOIN dbo.BusinessConfigurations k4
    ON k4.BusinessId = b.BusinessId AND k4.[Key] = 4
WHERE b.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.BusinessConfigurations existing
      WHERE existing.BusinessId = b.BusinessId AND existing.[Key] = 2
  )
  AND (k9.BusinessConfigurationId IS NOT NULL OR k4.BusinessConfigurationId IS NOT NULL);

-- Si hay negocio sin ninguna key legacy ni Key=2, no insertamos aquí (SeedSchedulingPolicy lo cubre)

DELETE FROM dbo.BusinessConfigurations WHERE [Key] IN (4, 9);

COMMIT;

PRINT N'MigrateSchedulingPolicy: completado.';
GO
