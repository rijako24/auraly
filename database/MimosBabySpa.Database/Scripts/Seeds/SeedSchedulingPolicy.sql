-- ============================================================
-- Script: SeedSchedulingPolicy
-- Política de agendamiento (Key=2) para negocios activos.
-- Horarios Mimos: lun–vie 8–12 / 14–18, sáb 8–13, dom cerrado.
-- Idempotente (MERGE por BusinessId + Key).
-- ============================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @SchedulingPolicyValue NVARCHAR(MAX) = N'{
  "slotIntervalMinutes": 60,
  "bufferBetweenAppointmentsMinutes": 0,
  "requireEmployee": true,
  "employeeStrategy": "least_versatile",
  "schedule": {
    "monday":    [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
    "tuesday":   [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
    "wednesday": [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
    "thursday":  [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
    "friday":    [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
    "saturday":  [{"open":"08:00","close":"13:00"}],
    "sunday":    []
  }
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
        [Description] = N'Política de agendamiento: horarios por día, intervalo de slots y reglas de empleado',
        UpdatedAt = GETUTCDATE(),
        IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (
        NEWID(),
        src.BusinessId,
        2,
        @SchedulingPolicyValue,
        N'Política de agendamiento: horarios por día, intervalo de slots y reglas de empleado',
        1,
        GETUTCDATE()
    );

PRINT N'SeedSchedulingPolicy: Key=2 (SchedulingPolicy) aplicada a negocios activos.';
GO
