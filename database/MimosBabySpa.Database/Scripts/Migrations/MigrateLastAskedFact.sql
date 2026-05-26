-- =============================================================================
-- MigrateLastAskedFact.sql
-- Agrega la columna LastAskedFact a ConversationStates para NEXT MOVE.
-- Idempotente.
-- =============================================================================

SET NOCOUNT ON;

IF COL_LENGTH('dbo.ConversationStates', 'LastAskedFact') IS NULL
BEGIN
    ALTER TABLE dbo.ConversationStates ADD [LastAskedFact] NVARCHAR(200) NULL;
    PRINT N'MigrateLastAskedFact: LastAskedFact column added.';
END
ELSE
    PRINT N'MigrateLastAskedFact: LastAskedFact column already exists.';
GO
