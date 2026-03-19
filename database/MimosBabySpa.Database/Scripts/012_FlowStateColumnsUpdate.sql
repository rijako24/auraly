-- =============================================================================
-- 012_FlowStateColumnsUpdate.sql
-- Adds ConversationHistoryJson and ConsecutiveDegradedTurns to FlowExecutionStates.
--
-- ConversationHistoryJson  : Serialized list of recent user+assistant messages.
--                            Null when history is empty.
-- ConsecutiveDegradedTurns : Counter of turns with no extracted variables.
--                            Used for automatic escalation.
-- =============================================================================

IF NOT EXISTS (
    SELECT 1
    FROM   sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.FlowExecutionStates')
      AND  name      = N'ConversationHistoryJson'
)
BEGIN
    ALTER TABLE dbo.FlowExecutionStates
        ADD ConversationHistoryJson NVARCHAR(MAX) NULL;

    PRINT 'Column ConversationHistoryJson added to FlowExecutionStates.';
END
ELSE
BEGIN
    PRINT 'Column ConversationHistoryJson already exists — skipping.';
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM   sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.FlowExecutionStates')
      AND  name      = N'ConsecutiveDegradedTurns'
)
BEGIN
    ALTER TABLE dbo.FlowExecutionStates
        ADD ConsecutiveDegradedTurns INT NOT NULL DEFAULT (0);

    PRINT 'Column ConsecutiveDegradedTurns added to FlowExecutionStates.';
END
ELSE
BEGIN
    PRINT 'Column ConsecutiveDegradedTurns already exists — skipping.';
END
GO
