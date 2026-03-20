-- =============================================================================
-- 032_ConversationsDropLegacyColumns.sql
-- Elimina columnas legacy no usadas por el Generic Flow (estado vive en
-- FlowExecutionStates; contexto en mensajes / variables de flujo).
-- =============================================================================

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'IX_Conversations_State'
)
BEGIN
    DROP INDEX [IX_Conversations_State] ON [dbo].[Conversations];
    PRINT 'Dropped IX_Conversations_State.';
END
GO

-- LastMessage se conserva / se reañade vía 033_ConversationsLastMessage.sql (preview admin).

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastIntent')
BEGIN
    ALTER TABLE [dbo].[Conversations] DROP COLUMN [LastIntent];
    PRINT 'Dropped Conversations.LastIntent.';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'BabyAge')
BEGIN
    ALTER TABLE [dbo].[Conversations] DROP COLUMN [BabyAge];
    PRINT 'Dropped Conversations.BabyAge.';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'RecommendedPlan')
BEGIN
    ALTER TABLE [dbo].[Conversations] DROP COLUMN [RecommendedPlan];
    PRINT 'Dropped Conversations.RecommendedPlan.';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'State')
BEGIN
    ALTER TABLE [dbo].[Conversations] DROP COLUMN [State];
    PRINT 'Dropped Conversations.State.';
END
GO

PRINT '=== Migration 032 completed. ===';
GO
