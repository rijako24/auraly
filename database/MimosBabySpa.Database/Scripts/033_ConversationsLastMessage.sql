-- =============================================================================
-- 033_ConversationsLastMessage.sql
-- Preview en admin: último texto del usuario. Mantenido por MessageService al
-- guardar mensajes con sender = User.
-- =============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastMessage'
)
BEGIN
    ALTER TABLE [dbo].[Conversations]
        ADD [LastMessage] NVARCHAR(1000) NULL;
    PRINT 'Column LastMessage added to Conversations.';
END
ELSE
BEGIN
    PRINT 'Column LastMessage already exists — skipping.';
END
GO

-- Vista admin: rellenar desde el último mensaje del usuario (mismo criterio que MessageService)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Conversations') AND name = N'LastMessage')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Messages' AND schema_id = SCHEMA_ID(N'dbo'))
BEGIN
    ;WITH LastUserMsg AS (
        SELECT
            m.[ConversationId],
            m.[MessageText],
            ROW_NUMBER() OVER (PARTITION BY m.[ConversationId] ORDER BY m.[Timestamp] DESC) AS rn
        FROM [dbo].[Messages] m
        WHERE m.[Sender] = N'User'
    )
    UPDATE c
    SET c.[LastMessage] = LEFT(lu.[MessageText], 1000)
    FROM [dbo].[Conversations] c
    INNER JOIN LastUserMsg lu ON lu.[ConversationId] = c.[ConversationId] AND lu.rn = 1
    WHERE c.[LastMessage] IS NULL OR LTRIM(RTRIM(c.[LastMessage])) = N'';

    PRINT 'Backfill LastMessage from Messages (last user message) applied where empty.';
END
GO

PRINT '=== Migration 033 completed. ===';
GO
