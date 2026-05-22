-- =============================================================================
-- MigrateBusinessConfigurationKeys.sql  (ONE-SHOT — ejecutar una sola vez)
--
-- Reasigna las claves de BusinessConfigurations al nuevo enum compactado:
--   Integrations:              6 → 0
--   PaymentConfirmationMessages: 8 → 1
--
-- Borra todas las demás entradas que corresponden a keys legacy del motor
-- de flujos ya removido (Personality=0, EntityExtraction=1, SalesStrategy=2,
-- PaymentConfig=3, OperatingHours=4, PaymentMethods=5, EscalationContacts=7).
--
-- NO incluir en PostDeployment.sql:
--   - Esta migración es solo para bases de datos productivas ya existentes.
--   - Las bases de datos creadas desde cero con Publish.ps1 tendrán los datos
--     correctos desde los seeds (SeedDevBusiness.sql).
--
-- Seguro ejecutar multiples veces (idempotente): DELETE filtra por keys que
-- existen; las UPDATE solo afectan filas con [Key] IN (6,8).
-- =============================================================================

BEGIN TRANSACTION;

-- Eliminar keys legacy ya no soportadas por el backend
DELETE FROM dbo.BusinessConfigurations
WHERE [Key] IN (0, 1, 2, 3, 4, 5, 7);

-- Reasignar Integrations: 6 -> 0
UPDATE dbo.BusinessConfigurations SET [Key] = 0 WHERE [Key] = 6;

-- Reasignar PaymentConfirmationMessages: 8 -> 1
UPDATE dbo.BusinessConfigurations SET [Key] = 1 WHERE [Key] = 8;

COMMIT;

PRINT N'MigrateBusinessConfigurationKeys: completado.';
GO
