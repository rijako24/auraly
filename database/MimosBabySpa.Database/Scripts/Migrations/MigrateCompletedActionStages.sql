-- =============================================================================
-- MigrateCompletedActionStages.sql
--
-- Agrega la columna CompletedActionStagesJson a ConversationStates para rastrear
-- etapas de acción (ExecutesTool) que completaron su tool exitosamente.
-- Permite al motor avanzar de checkout → closure sin depender del anticipo.
-- Idempotente: solo modifica si la columna no existe.
-- =============================================================================

IF NOT EXISTS (
    SELECT 1
    FROM   sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.ConversationStates')
      AND  name      = N'CompletedActionStagesJson'
)
BEGIN
    ALTER TABLE dbo.ConversationStates
    ADD CompletedActionStagesJson NVARCHAR(MAX) NULL;

    PRINT 'Column CompletedActionStagesJson added to ConversationStates.';
END
ELSE
BEGIN
    PRINT 'Column CompletedActionStagesJson already exists in ConversationStates — skipping.';
END
