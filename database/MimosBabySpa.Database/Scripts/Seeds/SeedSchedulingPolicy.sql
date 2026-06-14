-- ============================================================
-- Script: SeedSchedulingPolicy
-- Politica de agendamiento (Key=2) para negocios activos.
-- Solo reglas globales; los horarios viven en BusinessWorkingHours
-- y EmployeeWorkingHours.
-- Idempotente (MERGE por BusinessId + Key).
-- ============================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @SchedulingPolicyValue NVARCHAR(MAX) = N'{
  "slotIntervalMinutes": 60,
  "bufferBetweenAppointmentsMinutes": 0,
  "requireEmployee": true,
  "employeeStrategy": "least_versatile"
}';

MERGE dbo.BusinessConfigurations AS target
USING (
    SELECT b.BusinessId
    FROM dbo.Businesses b
    WHERE b.IsActive = 1
) AS src
   ON target.BusinessId = src.BusinessId AND target.[Key] = 2
WHEN MATCHED THEN
    UPDATE SET
        [Value] = @SchedulingPolicyValue,
        [Description] = N'Politica de agendamiento: intervalo de slots, buffer y reglas de empleado',
        UpdatedAt = GETUTCDATE(),
        IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (
        NEWID(),
        src.BusinessId,
        2,
        @SchedulingPolicyValue,
        N'Politica de agendamiento: intervalo de slots, buffer y reglas de empleado',
        1,
        GETUTCDATE()
    );

PRINT N'SeedSchedulingPolicy: Key=2 (SchedulingPolicy) aplicada a negocios activos sin horarios.';
GO
