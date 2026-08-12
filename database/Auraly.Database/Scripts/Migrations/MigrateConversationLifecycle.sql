-- =============================================================================
-- MigrateConversationLifecycle.sql
-- Engagement lifecycle on Conversations + Lead email + cleanup legacy session columns.
-- Idempotent.
-- =============================================================================

SET NOCOUNT ON;

IF COL_LENGTH('dbo.Conversations', 'Status') IS NULL
    ALTER TABLE dbo.Conversations ADD [Status] TINYINT NOT NULL CONSTRAINT DF_Conversations_Status DEFAULT 0;

IF COL_LENGTH('dbo.Conversations', 'OpenedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Conversations ADD [OpenedAt] DATETIME2 NULL;
    UPDATE dbo.Conversations SET [OpenedAt] = [Timestamp] WHERE [OpenedAt] IS NULL;
    ALTER TABLE dbo.Conversations ALTER COLUMN [OpenedAt] DATETIME2 NOT NULL;
END

IF COL_LENGTH('dbo.Conversations', 'LastActivityAt') IS NULL
BEGIN
    ALTER TABLE dbo.Conversations ADD [LastActivityAt] DATETIME2 NULL;
    UPDATE dbo.Conversations SET [LastActivityAt] = [Timestamp] WHERE [LastActivityAt] IS NULL;
    ALTER TABLE dbo.Conversations ALTER COLUMN [LastActivityAt] DATETIME2 NOT NULL;
END

IF COL_LENGTH('dbo.Conversations', 'ClosedAt') IS NULL
    ALTER TABLE dbo.Conversations ADD [ClosedAt] DATETIME2 NULL;

IF COL_LENGTH('dbo.Conversations', 'CloseReason') IS NULL
    ALTER TABLE dbo.Conversations ADD [CloseReason] NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.Leads', 'CustomerEmail') IS NULL
    ALTER TABLE dbo.Leads ADD [CustomerEmail] NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.ConversationStates', 'PreviousSessionJson') IS NOT NULL
    ALTER TABLE dbo.ConversationStates DROP COLUMN [PreviousSessionJson];

IF COL_LENGTH('dbo.ConversationStates', 'SessionStartedAt') IS NOT NULL
    ALTER TABLE dbo.ConversationStates DROP COLUMN [SessionStartedAt];

IF COL_LENGTH('dbo.Conversations', 'State') IS NOT NULL
BEGIN
    DROP INDEX IF EXISTS [IX_Conversations_State] ON dbo.Conversations;
    ALTER TABLE dbo.Conversations DROP COLUMN [State];
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_Conversations_OneActivePerCustomer'
      AND object_id = OBJECT_ID(N'dbo.Conversations'))
BEGIN
    CREATE UNIQUE INDEX [UX_Conversations_OneActivePerCustomer]
    ON dbo.Conversations ([BusinessId], [UserNumber])
    WHERE [Status] = 0;
    PRINT N'MigrateConversationLifecycle: created UX_Conversations_OneActivePerCustomer.';
END
ELSE
    PRINT N'MigrateConversationLifecycle: UX_Conversations_OneActivePerCustomer already exists.';
GO
