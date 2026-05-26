-- =============================================================================
-- SeedMimiDeleteLegacyServices.sql
--
-- Elimina servicios duplicados/legacy del negocio dev (aliases sin prefijo Plan).
-- Reasigna reservas al servicio canónico equivalente antes de borrar.
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    PRINT N'SeedMimiDeleteLegacyServices: business 22222222 not found - skipping.';
    RETURN;
END

DECLARE @LegacyMarineritos UNIQUEIDENTIFIER = '46F4A2B0-6F07-4356-B54D-4ED14AAA63AA';
DECLARE @LegacyAventuras UNIQUEIDENTIFIER = '552C435C-68DD-4DA3-B103-AFF693790FA7';
DECLARE @LegacySuaves UNIQUEIDENTIFIER = '49D0B849-E912-4D30-9FFD-5778EA7A3EE8';
DECLARE @LegacyClaseGrupal UNIQUEIDENTIFIER = 'A683D32D-5F6D-4E47-97B3-C13F0D9075A7';

DECLARE @PlanMarineritos UNIQUEIDENTIFIER = 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA';
DECLARE @PlanAventuras UNIQUEIDENTIFIER = 'AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA';
DECLARE @PlanPostVacunas UNIQUEIDENTIFIER = 'AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA';
DECLARE @Taller1Dia UNIQUEIDENTIFIER = 'AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA';

DECLARE @LegacyIds TABLE (LegacyId UNIQUEIDENTIFIER NOT NULL, CanonicalId UNIQUEIDENTIFIER NOT NULL);
INSERT INTO @LegacyIds (LegacyId, CanonicalId) VALUES
    (@LegacyMarineritos, @PlanMarineritos),
    (@LegacyAventuras, @PlanAventuras),
    (@LegacySuaves, @PlanPostVacunas),
    (@LegacyClaseGrupal, @Taller1Dia);

-- Reasignar reservas
UPDATE r
SET r.ServiceId = m.CanonicalId,
    r.UpdatedAt = SYSUTCDATETIME()
FROM dbo.Reservations r
INNER JOIN @LegacyIds m ON m.LegacyId = r.ServiceId
WHERE r.BusinessId = @BusinessId;

-- Reglas de add-on que apuntan al servicio legacy como compatible
UPDATE sar
SET sar.CompatibleServiceId = m.CanonicalId
FROM dbo.ServiceAddOnRules sar
INNER JOIN @LegacyIds m ON m.LegacyId = sar.CompatibleServiceId
WHERE sar.BusinessId = @BusinessId;

-- Usos de recursos y empleados
DELETE sru
FROM dbo.ServiceResourceUsages sru
INNER JOIN @LegacyIds m ON m.LegacyId = sru.ServiceId;

DELETE es
FROM dbo.EmployeeServices es
INNER JOIN @LegacyIds m ON m.LegacyId = es.ServiceId;

DELETE s
FROM dbo.Services s
INNER JOIN @LegacyIds m ON m.LegacyId = s.ServiceId
WHERE s.BusinessId = @BusinessId;

PRINT N'SeedMimiDeleteLegacyServices: legacy services removed for business ' + CAST(@BusinessId AS NVARCHAR(36));
GO
