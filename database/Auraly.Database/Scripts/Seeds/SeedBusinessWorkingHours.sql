-- ============================================================

-- SeedBusinessWorkingHours

-- Horario base para negocios activos sin horario configurado.

-- lun-vie 8-12 / 14-18, sab 8-13, dom cerrado.

-- ============================================================



SET NOCOUNT ON;



INSERT INTO dbo.BusinessWorkingHours (

    BusinessWorkingHourId, BusinessId, DayOfWeek, OpenTime, CloseTime, IsActive, CreatedAt)

SELECT NEWID(), b.BusinessId, v.DayOfWeek, v.OpenTime, v.CloseTime, 1, GETUTCDATE()

FROM dbo.Businesses b

CROSS APPLY (VALUES

    (1, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '12:00')),

    (1, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

    (2, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '12:00')),

    (2, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

    (3, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '12:00')),

    (3, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

    (4, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '12:00')),

    (4, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

    (5, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '12:00')),

    (5, CONVERT(TIME(0), '14:00'), CONVERT(TIME(0), '18:00')),

    (6, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '13:00'))

) AS v(DayOfWeek, OpenTime, CloseTime)

WHERE b.IsActive = 1

  AND NOT EXISTS (

      SELECT 1

      FROM dbo.BusinessWorkingHours existing

      WHERE existing.BusinessId = b.BusinessId

  );



PRINT N'SeedBusinessWorkingHours: horarios base aplicados a negocios activos sin horario.';

GO

