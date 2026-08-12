-- =============================================================================
-- MigratePaymentSnapshotAndReservationCustomAttrs.sql
-- Limpia snapshots legacy de PaymentTransactions y garantiza atributos custom
-- por tenant en Reservations. Idempotente.
-- =============================================================================

SET NOCOUNT ON;

-- PaymentTransactions: campos operativos vigentes
IF COL_LENGTH('dbo.PaymentTransactions', 'RequiresRescheduling') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [RequiresRescheduling] BIT NOT NULL
        CONSTRAINT DF_PaymentTransactions_RequiresRescheduling DEFAULT 0;

IF COL_LENGTH('dbo.PaymentTransactions', 'RequiresRefund') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [RequiresRefund] BIT NOT NULL
        CONSTRAINT DF_PaymentTransactions_RequiresRefund DEFAULT 0;

-- PaymentTransactions: remover snapshot legacy; CheckoutSnapshotJson es la fuente vigente.
IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_PaymentTransactions_SnapshotService'
      AND parent_object_id = OBJECT_ID('dbo.PaymentTransactions'))
BEGIN
    ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT [FK_PaymentTransactions_SnapshotService];
END

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_PaymentTransactions_Snapshot_Slot'
      AND object_id = OBJECT_ID('dbo.PaymentTransactions'))
BEGIN
    DROP INDEX [IX_PaymentTransactions_Snapshot_Slot] ON dbo.PaymentTransactions;
END

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomAttributesJson') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_CustomAttributesJson];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_AddOnIds') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_AddOnIds];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomerPhone') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_CustomerPhone];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomerEmail') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_CustomerEmail];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomerName') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_CustomerName];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_DurationMinutes') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_DurationMinutes];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_PreferredEmployeeId') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_PreferredEmployeeId];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_ReservationDateTime') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_ReservationDateTime];

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_ServiceId') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP COLUMN [Snapshot_ServiceId];

-- Reservations: atributos custom del tenant
IF COL_LENGTH('dbo.Reservations', 'CustomAttributesJson') IS NULL
    ALTER TABLE dbo.Reservations ADD [CustomAttributesJson] NVARCHAR(MAX) NULL;

PRINT 'MigratePaymentSnapshotAndReservationCustomAttrs completed.';