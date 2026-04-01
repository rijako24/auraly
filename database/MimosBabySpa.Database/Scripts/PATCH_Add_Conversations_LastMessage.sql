-- =============================================================================
-- Parche: añade LastMessage si falta (misma definición que Tables/Conversations.sql).
-- Ejecutar si ya corriste ROLLBACK_to_028.sql antes de que incluyera este paso,
-- o si ves: Invalid column name 'LastMessage'.
-- Idempotente.
-- =============================================================================

SET NOCOUNT ON;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastMessage'
)
BEGIN
    ALTER TABLE [dbo].[Conversations] ADD [LastMessage] NVARCHAR(1000) NULL;
    PRINT 'Columna LastMessage añadida.';
END
ELSE
    PRINT 'LastMessage ya existe — nada que hacer.';
GO
