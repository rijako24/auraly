-- ============================================================
-- MigrateWorkingHoursFromSchedulingPolicy
-- Copia BusinessConfigurations.Key=SchedulingPolicy.schedule a
-- BusinessWorkingHours y remueve schedule del JSON de reglas.
-- Idempotente: solo crea horarios para negocios sin filas previas.
-- ============================================================

SET NOCOUNT ON;

;WITH Days AS (
    SELECT N'sunday' AS [Name], 0 AS DayOfWeek UNION ALL
    SELECT N'monday', 1 UNION ALL
    SELECT N'tuesday', 2 UNION ALL
    SELECT N'wednesday', 3 UNION ALL
    SELECT N'thursday', 4 UNION ALL
    SELECT N'friday', 5 UNION ALL
    SELECT N'saturday', 6
),
SourceBlocks AS (
    SELECT
        bc.BusinessId,
        d.DayOfWeek,
        TRY_CONVERT(TIME(0), JSON_VALUE(blocks.[value], '$.open')) AS OpenTime,
        TRY_CONVERT(TIME(0), JSON_VALUE(blocks.[value], '$.close')) AS CloseTime
    FROM dbo.BusinessConfigurations bc
    CROSS JOIN Days d
    CROSS APPLY OPENJSON(JSON_QUERY(bc.[Value], CONCAT('$.schedule.', d.[Name]))) blocks
    WHERE bc.[Key] = 2
      AND ISJSON(bc.[Value]) = 1
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.BusinessWorkingHours existing
          WHERE existing.BusinessId = bc.BusinessId
      )
)
INSERT INTO dbo.BusinessWorkingHours (
    BusinessWorkingHourId, BusinessId, DayOfWeek, OpenTime, CloseTime, IsActive, CreatedAt)
SELECT NEWID(), BusinessId, DayOfWeek, OpenTime, CloseTime, 1, GETUTCDATE()
FROM SourceBlocks
WHERE OpenTime IS NOT NULL
  AND CloseTime IS NOT NULL
  AND OpenTime < CloseTime;

UPDATE dbo.BusinessConfigurations
SET [Value] = JSON_MODIFY([Value], '$.schedule', NULL),
    [Description] = N'Reglas de agendamiento: intervalo de slots, buffer y estrategia de empleado',
    UpdatedAt = GETUTCDATE()
WHERE [Key] = 2
  AND ISJSON([Value]) = 1
  AND JSON_QUERY([Value], '$.schedule') IS NOT NULL;

PRINT N'MigrateWorkingHoursFromSchedulingPolicy: horarios migrados a BusinessWorkingHours.';
GO
