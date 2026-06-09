-- ============================================================
-- Script: CleanupObsoleteBookingPolicy
-- BookingPolicy (Key=3) fue eliminado: las reglas de pago viven
-- en Agents.SettingsJson.checkout.
-- ============================================================

SET NOCOUNT ON;

DELETE FROM dbo.BusinessConfigurations
WHERE [Key] = 3;

PRINT N'CleanupObsoleteBookingPolicy: BusinessConfigurations Key=3 eliminada.';
GO
