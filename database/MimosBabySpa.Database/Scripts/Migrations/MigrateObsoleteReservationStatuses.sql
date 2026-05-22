-- =============================================================================
-- MigrateObsoleteReservationStatuses.sql
-- Marca como Expired las reservas en estados obsoletos del flujo draft
-- (Draft=10, AvailabilityVerified=11, PendingPayment=12).
-- Idempotente: solo actualiza filas en esos estados.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @Affected INT;

UPDATE dbo.Reservations
SET [Status] = 91,
    [UpdatedAt] = GETUTCDATE()
WHERE [Status] IN (10, 11, 12);

SET @Affected = @@ROWCOUNT;

IF @Affected > 0
    PRINT N'MigrateObsoleteReservationStatuses: ' + CAST(@Affected AS NVARCHAR(10))
        + N' reservation(s) moved from obsolete statuses (10/11/12) to Expired (91).';
ELSE
    PRINT N'MigrateObsoleteReservationStatuses: no obsolete reservation statuses found — skipped.';
GO
