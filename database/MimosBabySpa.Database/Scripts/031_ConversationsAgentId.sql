-- =============================================================================
-- 031_ConversationsAgentId.sql
--
-- AgentId en Conversations: vínculo al agente del canal (p. ej. WhatsApp) para
-- resolver FlowExecutionState sin iterar todos los agentes del negocio.
--
-- Backfill: solo cuando hay un único AgentId distinto entre números WhatsApp
-- activos del negocio (evita asignar mal si hay varios canales/agentes).
-- =============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.Conversations')
      AND  name      = N'AgentId'
)
BEGIN
    ALTER TABLE dbo.Conversations
        ADD AgentId UNIQUEIDENTIFIER NULL
        CONSTRAINT FK_Conversations_Agents
            FOREIGN KEY REFERENCES dbo.Agents(AgentId)
            ON DELETE SET NULL;

    PRINT 'Column AgentId added to Conversations.';
END
ELSE
BEGIN
    PRINT 'Column AgentId already exists in Conversations — skipping add.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  object_id = OBJECT_ID(N'dbo.Conversations')
      AND  name      = N'IX_Conversations_AgentId'
)
BEGIN
    CREATE INDEX IX_Conversations_AgentId ON dbo.Conversations (AgentId);
    PRINT 'Index IX_Conversations_AgentId created.';
END
GO

;WITH SingleAgentPerBusiness AS (
    SELECT BusinessId,
           MIN(AgentId) AS AgentId
    FROM dbo.BusinessWhatsAppNumbers
    WHERE IsActive = 1
      AND AgentId IS NOT NULL
    GROUP BY BusinessId
    HAVING COUNT(DISTINCT AgentId) = 1
)
UPDATE c
SET    c.AgentId = s.AgentId
FROM   dbo.Conversations AS c
INNER JOIN SingleAgentPerBusiness AS s ON s.BusinessId = c.BusinessId
WHERE  c.AgentId IS NULL;

PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' conversation(s) backfilled with AgentId (single-agent businesses).';
GO

PRINT '=== Migration 031 completed. ===';
GO
