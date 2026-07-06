-- Adds the active request boundary used by the agent runtime to project
-- only the current request history into LLM context.

IF COL_LENGTH('dbo.ConversationStates', 'ActiveRequestStartedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.ConversationStates ADD [ActiveRequestStartedAtUtc] DATETIME2 NULL;
    PRINT N'MigrateActiveRequestBoundary: ActiveRequestStartedAtUtc column added.';
END
ELSE
BEGIN
    PRINT N'MigrateActiveRequestBoundary: ActiveRequestStartedAtUtc column already exists.';
END