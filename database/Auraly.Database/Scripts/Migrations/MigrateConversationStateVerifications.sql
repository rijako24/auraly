-- =============================================================================
-- MigrateConversationStateVerifications.sql
-- Mueve verificaciones del agente a ConversationStates.VerificationsJson
-- y elimina la tabla ConversationVerifications si existe. Idempotente.
-- =============================================================================

SET NOCOUNT ON;

IF COL_LENGTH('dbo.ConversationStates', 'VerificationsJson') IS NULL
    ALTER TABLE dbo.ConversationStates ADD [VerificationsJson] NVARCHAR(MAX) NULL;

IF OBJECT_ID(N'dbo.ConversationVerifications', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.ConversationVerifications;
    PRINT N'MigrateConversationStateVerifications: dropped ConversationVerifications table.';
END
ELSE
    PRINT N'MigrateConversationStateVerifications: ConversationVerifications table not found — skipped drop.';

IF COL_LENGTH('dbo.ConversationStates', 'VerificationsJson') IS NOT NULL
    PRINT N'MigrateConversationStateVerifications: VerificationsJson column ready.';
GO
