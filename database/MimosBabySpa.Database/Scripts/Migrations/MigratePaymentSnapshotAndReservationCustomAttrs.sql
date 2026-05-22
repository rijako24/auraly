-- =============================================================================
-- MigratePaymentSnapshotAndReservationCustomAttrs.sql
-- Snapshot inmutable del intent de reserva en PaymentTransactions + atributos
-- custom dinámicos por tenant en Reservations. Idempotente.
-- =============================================================================

SET NOCOUNT ON;

-- ── PaymentTransactions: snapshot del intent ─────────────────────────────────
IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_ServiceId') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_ServiceId] UNIQUEIDENTIFIER NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_ReservationDateTime') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_ReservationDateTime] DATETIME2 NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_PreferredEmployeeId') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_PreferredEmployeeId] UNIQUEIDENTIFIER NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_DurationMinutes') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_DurationMinutes] INT NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomerName') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_CustomerName] NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomerEmail') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_CustomerEmail] NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomerPhone') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_CustomerPhone] NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_AddOnIds') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_AddOnIds] NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'Snapshot_CustomAttributesJson') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [Snapshot_CustomAttributesJson] NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'RequiresRescheduling') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [RequiresRescheduling] BIT NOT NULL
        CONSTRAINT DF_PaymentTransactions_RequiresRescheduling DEFAULT 0;

IF COL_LENGTH('dbo.PaymentTransactions', 'RequiresRefund') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [RequiresRefund] BIT NOT NULL
        CONSTRAINT DF_PaymentTransactions_RequiresRefund DEFAULT 0;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_PaymentTransactions_SnapshotService'
      AND parent_object_id = OBJECT_ID('dbo.PaymentTransactions'))
BEGIN
    ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT [FK_PaymentTransactions_SnapshotService]
        FOREIGN KEY ([Snapshot_ServiceId]) REFERENCES [dbo].[Services] ([ServiceId]) ON DELETE NO ACTION;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_PaymentTransactions_Snapshot_Slot'
      AND object_id = OBJECT_ID('dbo.PaymentTransactions'))
BEGIN
    CREATE INDEX [IX_PaymentTransactions_Snapshot_Slot]
        ON dbo.PaymentTransactions ([Snapshot_ServiceId], [Snapshot_ReservationDateTime])
        WHERE [Snapshot_ServiceId] IS NOT NULL;
END

-- ── Reservations: atributos custom del tenant ────────────────────────────────
IF COL_LENGTH('dbo.Reservations', 'CustomAttributesJson') IS NULL
    ALTER TABLE dbo.Reservations ADD [CustomAttributesJson] NVARCHAR(MAX) NULL;

PRINT 'MigratePaymentSnapshotAndReservationCustomAttrs completed.';
