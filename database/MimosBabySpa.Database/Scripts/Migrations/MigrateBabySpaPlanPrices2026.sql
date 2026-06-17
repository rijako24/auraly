-- =============================================================================
-- MigrateBabySpaPlanPrices2026.sql
--
-- Actualiza los planes principales de Mimo's Baby Spa con precios, duraciones
-- e inclusiones vigentes.
-- =============================================================================

SET NOCOUNT ON;

UPDATE dbo.Services
SET Price = 125000,
    DurationMinutes = 60,
    Description = N'Estimulación temprana, hidroterapia y masaje infantil.',
    UpdatedAt = SYSUTCDATETIME()
WHERE ServiceName = N'Plan Marineritos'
   OR ServiceName LIKE N'%Marineritos%';

UPDATE dbo.Services
SET Price = 100000,
    DurationMinutes = 45,
    Description = N'Hidroterapia y masaje infantil.',
    UpdatedAt = SYSUTCDATETIME()
WHERE ServiceName = N'Plan Aventuras Marinas'
   OR ServiceName LIKE N'%Aventuras Marinas%';

UPDATE dbo.Services
SET ServiceName = N'Plan Suaves Mimos - Post Vacunas',
    Price = 95000,
    DurationMinutes = 45,
    Description = N'Hidroterapia relajante y masaje infantil. No se toca la zona de punción.',
    UpdatedAt = SYSUTCDATETIME()
WHERE ServiceName = N'Plan Suaves Mimos'
   OR ServiceName = N'Plan Post Vacunas'
   OR ServiceName = N'Plan Suaves Mimos - Post Vacunas'
   OR ServiceName LIKE N'%Suaves Mimos%'
   OR ServiceName LIKE N'%Post Vacunas%';

PRINT N'MigrateBabySpaPlanPrices2026: planes Baby Spa actualizados.';
GO
