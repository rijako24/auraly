-- =============================================================================
-- MigratePaymentSupersession.sql
-- Tracks superseded checkout links when booking intent changes.
-- Idempotent.
-- =============================================================================

SET NOCOUNT ON;

IF COL_LENGTH('dbo.PaymentTransactions', 'SupersededAt') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [SupersededAt] DATETIME2 NULL;

IF COL_LENGTH('dbo.PaymentTransactions', 'SupersededByPaymentTransactionId') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [SupersededByPaymentTransactionId] UNIQUEIDENTIFIER NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_PaymentTransactions_SupersededBy')
BEGIN
    ALTER TABLE dbo.PaymentTransactions
    ADD CONSTRAINT [FK_PaymentTransactions_SupersededBy]
        FOREIGN KEY ([SupersededByPaymentTransactionId])
        REFERENCES [dbo].[PaymentTransactions] ([PaymentTransactionId])
        ON DELETE NO ACTION;
END

PRINT N'MigratePaymentSupersession: supersession columns ready.';
GO
