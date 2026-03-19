-- =============================================================================
-- 014_AgentIdOnWhatsAppNumber.sql
--
-- Adds AgentId to BusinessWhatsAppNumbers so that when a WhatsApp message
-- arrives, the agent responsible for that number is resolved in a single query,
-- without needing AgentChannelAssignments.
--
-- Also drops AgentChannelAssignments (replaced by the column above).
-- =============================================================================

-- ── 1. Add AgentId to BusinessWhatsAppNumbers ─────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.BusinessWhatsAppNumbers')
      AND  name      = N'AgentId'
)
BEGIN
    ALTER TABLE dbo.BusinessWhatsAppNumbers
        ADD AgentId UNIQUEIDENTIFIER NULL
        CONSTRAINT FK_BusinessWhatsAppNumbers_Agent
            FOREIGN KEY REFERENCES dbo.Agents(AgentId)
            ON DELETE SET NULL;

    PRINT 'Column AgentId added to BusinessWhatsAppNumbers.';
END
ELSE
BEGIN
    PRINT 'Column AgentId already exists in BusinessWhatsAppNumbers — skipping.';
END
GO

-- ── 2. Index for fast agent lookup ───────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  object_id = OBJECT_ID(N'dbo.BusinessWhatsAppNumbers')
      AND  name      = N'IX_BusinessWhatsAppNumbers_AgentId'
)
BEGIN
    CREATE INDEX IX_BusinessWhatsAppNumbers_AgentId
        ON dbo.BusinessWhatsAppNumbers (AgentId);
    PRINT 'Index IX_BusinessWhatsAppNumbers_AgentId created.';
END
GO

-- ── 3. Assign AgentId to Mimo Bot's WhatsApp number ──────────────────────────
DECLARE @AgentId UNIQUEIDENTIFIER = '7105A9D5-D4E4-4BBA-9F3A-DBB34E0B1B86';

UPDATE dbo.BusinessWhatsAppNumbers
SET    AgentId = @AgentId
WHERE  AgentId IS NULL
  AND  BusinessId = (SELECT BusinessId FROM dbo.Agents WHERE AgentId = @AgentId);

PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' BusinessWhatsAppNumbers row(s) linked to Mimo Bot agent.';
GO

-- ── 4. Drop AgentChannelAssignments ─────────────────────────────────────────
--    The channel → agent relationship now lives in BusinessWhatsAppNumbers.AgentId.
--    Keep table only if other channel types (Webchat, Telegram) still reference it;
--    for this project it is safe to drop.
IF EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'AgentChannelAssignments'
)
BEGIN
    DROP TABLE dbo.AgentChannelAssignments;
    PRINT 'Table AgentChannelAssignments dropped.';
END
ELSE
BEGIN
    PRINT 'Table AgentChannelAssignments does not exist — skipping.';
END
GO

PRINT '=== Migration 014 completed. ===';
GO
