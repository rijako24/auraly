SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH('dbo.ConversationStates', 'FollowUpDueAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.ConversationStates ADD [FollowUpDueAtUtc] DATETIME2 NULL;
    PRINT N'MigrateConversationFollowUp: FollowUpDueAtUtc column added.';
END
ELSE
BEGIN
    PRINT N'MigrateConversationFollowUp: FollowUpDueAtUtc column already exists.';
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = 'IX_ConversationStates_FollowUpDueAtUtc'
      AND [object_id] = OBJECT_ID('dbo.ConversationStates'))
BEGIN
    CREATE INDEX [IX_ConversationStates_FollowUpDueAtUtc]
        ON dbo.ConversationStates ([FollowUpDueAtUtc])
        WHERE [FollowUpDueAtUtc] IS NOT NULL;
    PRINT N'MigrateConversationFollowUp: due index created.';
END
ELSE
BEGIN
    PRINT N'MigrateConversationFollowUp: due index already exists.';
END
GO
