-- =============================================================================
-- MigrateCompletedOneShotStages.sql
--
-- Agrega la columna CompletedStagesJson a ConversationStates para rastrear
-- etapas con CompletesOnEnter=true que ya se ejecutaron (ej. saludo).
-- Idempotente: solo modifica si la columna no existe.
-- =============================================================================

IF NOT EXISTS (
    SELECT 1
    FROM   sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.ConversationStates')
      AND  name      = N'CompletedStagesJson'
)
BEGIN
    ALTER TABLE dbo.ConversationStates
    ADD CompletedStagesJson NVARCHAR(MAX) NULL;

    PRINT 'Column CompletedStagesJson added to ConversationStates.';
END
ELSE
BEGIN
    PRINT 'Column CompletedStagesJson already exists in ConversationStates — skipping.';
END
