-- =============================================================================
-- MigrateObsoleteReservationStatuses.sql
-- Limpia estados antiguos de reservas que pertenecian al flujo draft eliminado.
-- Draft=10, AvailabilityVerified=11 y PendingPayment=12 se cancelan porque el
-- intent actual vive en facts o PaymentTransactions, no en Reservations.
-- Idempotente: solo actualiza filas en esos estados.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @Affected INT;

UPDATE dbo.Reservations
SET [Status] = 3,
    [UpdatedAt] = GETUTCDATE()
WHERE [Status] IN (10, 11, 12, 91);

SET @Affected = @@ROWCOUNT;

IF @Affected > 0
    PRINT N'MigrateObsoleteReservationStatuses: ' + CAST(@Affected AS NVARCHAR(10))
        + N' reservation(s) moved from removed reservation statuses to Cancelled (3).';
ELSE
    PRINT N'MigrateObsoleteReservationStatuses: no removed reservation statuses found - skipped.';
GO
