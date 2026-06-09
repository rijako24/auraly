-- =============================================================================
-- MigrateFlowStageSnapshots.sql
-- Agrega la columna StageSnapshotsJson a ConversationStates para persistir
-- snapshots de facts por etapa (soporte a ReentryOnFactChanged). Idempotente.
-- =============================================================================

SET NOCOUNT ON;

IF COL_LENGTH('dbo.ConversationStates', 'StageSnapshotsJson') IS NULL
BEGIN
    ALTER TABLE dbo.ConversationStates ADD [StageSnapshotsJson] NVARCHAR(MAX) NULL;
    PRINT N'MigrateFlowStageSnapshots: StageSnapshotsJson column added.';
END
ELSE
    PRINT N'MigrateFlowStageSnapshots: StageSnapshotsJson column already exists.';
GO
