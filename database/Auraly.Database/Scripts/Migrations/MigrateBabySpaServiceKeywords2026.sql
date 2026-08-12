-- =============================================================================
-- MigrateBabySpaServiceKeywords2026.sql
--
-- Configura sinonimos de busqueda para los servicios de Mimo's Baby Spa.
-- Mantiene los terminos como datos de catalogo, no como reglas quemadas.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @MimosBusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @IniciacionServiceName NVARCHAR(200) = N'Programa de Iniciaci' + NCHAR(243) + N'n al Jard' + NCHAR(237) + N'n';

UPDATE dbo.Services
SET Keywords = N'plan, planes, planes baby spa, baby spa, spa bebe, hidroterapia, masaje infantil, estimulacion temprana, relajacion, desarrollo sensorial, marineritos',
    UpdatedAt = SYSUTCDATETIME()
WHERE BusinessId = @MimosBusinessId
  AND IsActive = 1
  AND (ServiceName = N'Plan Marineritos' OR ServiceName LIKE N'%Marineritos%');

UPDATE dbo.Services
SET Keywords = N'plan, planes, planes baby spa, baby spa, spa bebe, hidroterapia, masaje infantil, aventura marina, estimulacion sensorial, relajacion, aventuras marinas',
    UpdatedAt = SYSUTCDATETIME()
WHERE BusinessId = @MimosBusinessId
  AND IsActive = 1
  AND (ServiceName = N'Plan Aventuras Marinas' OR ServiceName LIKE N'%Aventuras Marinas%');

UPDATE dbo.Services
SET Keywords = N'plan, planes, planes baby spa, baby spa, spa bebe, post vacunas, despues de vacunas, suave, mimos, masaje suave, relajacion, bienestar, hidroterapia suave',
    UpdatedAt = SYSUTCDATETIME()
WHERE BusinessId = @MimosBusinessId
  AND IsActive = 1
  AND (
        ServiceName = N'Plan Suaves Mimos - Post Vacunas'
        OR ServiceName LIKE N'%Suaves Mimos%'
        OR ServiceName LIKE N'%Post Vacunas%'
      );

UPDATE dbo.Services
SET Keywords = N'iniciacion al jardin, jardin infantil, preparacion jardin, adaptacion escolar, autonomia, socializacion, rutinas, lenguaje, motricidad, seguridad emocional, inscripcion, horario fijo',
    UpdatedAt = SYSUTCDATETIME()
WHERE BusinessId = @MimosBusinessId
  AND IsActive = 1
  AND (
        ServiceName = @IniciacionServiceName
        OR ServiceName LIKE N'Programa de Iniciaci%n al Jard%n'
        OR ServiceName LIKE N'%Iniciaci%n al Jard%n%'
      );

PRINT N'MigrateBabySpaServiceKeywords2026: keywords configurados.';
GO