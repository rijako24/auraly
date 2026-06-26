-- =============================================================================
-- SeedSolorzanoWorkingHours.sql
--
-- Configura el horario laboral de Vinos Artesanales Solorzano.
-- Domingo a sabado, 08:00 a 21:00.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @SolorzanoBusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @SolorzanoBusinessId)
BEGIN
    PRINT N'SeedSolorzanoWorkingHours: negocio Solorzano no encontrado; omitiendo.';
    RETURN;
END

DECLARE @Hours TABLE (DayOfWeek INT NOT NULL, OpenTime TIME(0) NOT NULL, CloseTime TIME(0) NOT NULL);
INSERT INTO @Hours (DayOfWeek, OpenTime, CloseTime)
VALUES
(0, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '21:00')),
(1, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '21:00')),
(2, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '21:00')),
(3, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '21:00')),
(4, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '21:00')),
(5, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '21:00')),
(6, CONVERT(TIME(0), '08:00'), CONVERT(TIME(0), '21:00'));

MERGE dbo.BusinessWorkingHours AS target
USING @Hours AS source
   ON target.BusinessId = @SolorzanoBusinessId
  AND target.DayOfWeek = source.DayOfWeek
  AND target.OpenTime = source.OpenTime
WHEN MATCHED THEN
    UPDATE SET CloseTime = source.CloseTime,
               IsActive = 1,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (BusinessWorkingHourId, BusinessId, DayOfWeek, OpenTime, CloseTime, IsActive, CreatedAt)
    VALUES (NEWID(), @SolorzanoBusinessId, source.DayOfWeek, source.OpenTime, source.CloseTime, 1, GETUTCDATE());

UPDATE dbo.BusinessWorkingHours
SET IsActive = 0,
    UpdatedAt = GETUTCDATE()
WHERE BusinessId = @SolorzanoBusinessId
  AND NOT EXISTS (
      SELECT 1
      FROM @Hours h
      WHERE h.DayOfWeek = BusinessWorkingHours.DayOfWeek
        AND h.OpenTime = BusinessWorkingHours.OpenTime
  );

PRINT N'SeedSolorzanoWorkingHours: horarios Solorzano configurados de domingo a sabado 08:00-21:00.';
GO
