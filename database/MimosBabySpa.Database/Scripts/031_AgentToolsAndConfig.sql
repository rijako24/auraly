-- =============================================================================
-- Migration 031: Agentic Engine — Agent tools & config columns
--
-- Adds the columns and table required by the new Function Calling architecture.
-- These changes are ADDITIVE — no existing data is modified or removed.
--
-- Changes:
--   1. Agents.Model          — Azure OpenAI deployment/model name per agent
--   2. Agents.Temperature    — LLM temperature per agent (0.0 – 1.0)
--   3. Agents.MaxToolIterations — anti-loop limit per agent (default 6)
-- NOTE: SystemPrompt, escalation contacts and tool list are stored in
--       Agents.SettingsJson (already exists) to avoid schema churn.
--       SettingsJson structure expected by AgentConfigProvider:
--       {
--         "model":                          "gpt-4o-mini",
--         "temperature":                    0.7,
--         "maxToolIterations":              6,
--         "consecutiveErrorEscalationThreshold": 3,
--         "enabledTools":                   ["check_availability", ...],
--         "escalation": { "contacts":       ["+573001234567"] }
--       }
--
--   4. AgentTools — table that enables/disables tools per agent
--      (alternative to SettingsJson array for admin UI control)
--
-- Idempotent: safe to re-run.
-- =============================================================================

BEGIN TRANSACTION;

-- ── 1. Add Model column to Agents ────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Agents') AND name = 'Model'
)
BEGIN
    ALTER TABLE Agents ADD Model NVARCHAR(100) NULL;
    PRINT 'Added Agents.Model';
END

-- ── 2. Add Temperature column to Agents ──────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Agents') AND name = 'Temperature'
)
BEGIN
    ALTER TABLE Agents ADD Temperature DECIMAL(3,2) NULL;
    PRINT 'Added Agents.Temperature';
END

-- ── 3. Add MaxToolIterations column to Agents ────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Agents') AND name = 'MaxToolIterations'
)
BEGIN
    ALTER TABLE Agents ADD MaxToolIterations INT NULL;
    PRINT 'Added Agents.MaxToolIterations';
END

-- ── 4. Create AgentTools table ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('AgentTools') AND type = 'U')
BEGIN
    CREATE TABLE AgentTools (
        AgentToolId    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        AgentId        UNIQUEIDENTIFIER NOT NULL,
        ToolName       NVARCHAR(100)    NOT NULL,
        IsEnabled      BIT              NOT NULL DEFAULT 1,
        DisplayOrder   INT              NOT NULL DEFAULT 0,
        CreatedAt      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt      DATETIME2        NULL,
        CONSTRAINT FK_AgentTools_Agents FOREIGN KEY (AgentId) REFERENCES Agents(AgentId) ON DELETE CASCADE,
        CONSTRAINT UQ_AgentTools_AgentTool UNIQUE (AgentId, ToolName)
    );
    PRINT 'Created AgentTools table';
END

-- ── 5. Default tool seeds for existing agents ────────────────────────────────
-- Seeds only if AgentTools is empty for an agent (idempotent via UNIQUE constraint)
DECLARE @DefaultTools TABLE (ToolName NVARCHAR(100), DisplayOrder INT);
INSERT INTO @DefaultTools VALUES
    ('check_availability',    1),
    ('resolve_pricing',       2),
    ('create_reservation',    3),
    ('reschedule_reservation',4),
    ('suspend_reservation',   5),
    ('generate_payment_link', 6),
    ('verify_payment',        7),
    ('escalate_to_human',     8),
    ('get_service_catalog',   9);

INSERT INTO AgentTools (AgentId, ToolName, IsEnabled, DisplayOrder)
SELECT a.AgentId, dt.ToolName, 1, dt.DisplayOrder
FROM Agents a
CROSS JOIN @DefaultTools dt
WHERE NOT EXISTS (
    SELECT 1 FROM AgentTools at2
    WHERE at2.AgentId = a.AgentId AND at2.ToolName = dt.ToolName
);

PRINT 'Seeded default tools for existing agents';

COMMIT TRANSACTION;
PRINT 'Migration 031 completed successfully.';
