-- =============================================================================
-- MigrateMultitenantStateArchitecture.sql
-- Evoluciona esquema existente hacia arquitectura multitenant de 3 capas.
-- Idempotente. Usa SQL dinámico para columnas legacy (SqlPackage ya puede
-- haberlas eliminado antes de ejecutar PostDeployment).
-- =============================================================================

SET NOCOUNT ON;

-- ── ConversationStates: JSON blob → columnar ─────────────────────────────────
IF COL_LENGTH('dbo.ConversationStates', 'StateJson') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ConversationStates', 'Owner') IS NULL
        ALTER TABLE dbo.ConversationStates ADD [Owner] TINYINT NOT NULL CONSTRAINT DF_ConversationStates_Owner DEFAULT 0;
    IF COL_LENGTH('dbo.ConversationStates', 'LastEscalatedAt') IS NULL
        ALTER TABLE dbo.ConversationStates ADD [LastEscalatedAt] DATETIME2 NULL;
    IF COL_LENGTH('dbo.ConversationStates', 'ConsecutiveDegradedTurns') IS NULL
        ALTER TABLE dbo.ConversationStates ADD [ConsecutiveDegradedTurns] INT NOT NULL CONSTRAINT DF_ConversationStates_Degraded DEFAULT 0;
    IF COL_LENGTH('dbo.ConversationStates', 'LastUserMessage') IS NULL
        ALTER TABLE dbo.ConversationStates ADD [LastUserMessage] NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.ConversationStates', 'LastBotMessage') IS NULL
        ALTER TABLE dbo.ConversationStates ADD [LastBotMessage] NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.ConversationStates', 'PreviousSessionJson') IS NULL
        ALTER TABLE dbo.ConversationStates ADD [PreviousSessionJson] NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.ConversationStates', 'SessionStartedAt') IS NULL
        ALTER TABLE dbo.ConversationStates ADD [SessionStartedAt] DATETIME2 NOT NULL CONSTRAINT DF_ConversationStates_Session DEFAULT GETUTCDATE();

    EXEC(N'
        UPDATE cs SET cs.[Owner] = TRY_CAST(JSON_VALUE(cs.StateJson, ''$.owner'') AS TINYINT)
        FROM dbo.ConversationStates cs
        WHERE cs.StateJson IS NOT NULL AND JSON_VALUE(cs.StateJson, ''$.owner'') IS NOT NULL;

        ALTER TABLE dbo.ConversationStates DROP COLUMN [StateJson];
    ');
END

-- ── Conversations: limpieza multitenant ──────────────────────────────────────
IF COL_LENGTH('dbo.Conversations', 'BabyAge') IS NOT NULL
BEGIN
    EXEC(N'
        INSERT INTO dbo.ConversationContexts (ConversationContextId, ConversationId, Field, Value, CreatedAt)
        SELECT NEWID(), c.ConversationId, N''baby_age_months'', CAST(c.BabyAge AS NVARCHAR(10)), SYSUTCDATETIME()
        FROM dbo.Conversations c
        WHERE c.BabyAge IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM dbo.ConversationContexts cc
              WHERE cc.ConversationId = c.ConversationId AND cc.Field = N''baby_age_months'');

        ALTER TABLE dbo.Conversations DROP COLUMN [BabyAge];
    ');
END

IF COL_LENGTH('dbo.Conversations', 'RecommendedPlan') IS NOT NULL
BEGIN
    EXEC(N'
        INSERT INTO dbo.ConversationContexts (ConversationContextId, ConversationId, Field, Value, CreatedAt)
        SELECT NEWID(), c.ConversationId, N''recommended_plan'', c.RecommendedPlan, SYSUTCDATETIME()
        FROM dbo.Conversations c
        WHERE c.RecommendedPlan IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM dbo.ConversationContexts cc
              WHERE cc.ConversationId = c.ConversationId AND cc.Field = N''recommended_plan'');

        ALTER TABLE dbo.Conversations DROP COLUMN [RecommendedPlan];
    ');
END

IF COL_LENGTH('dbo.Conversations', 'LastIntent') IS NOT NULL
    ALTER TABLE dbo.Conversations DROP COLUMN [LastIntent];

IF COL_LENGTH('dbo.Conversations', 'CustomerEmail') IS NULL
    ALTER TABLE dbo.Conversations ADD [CustomerEmail] NVARCHAR(200) NULL;

-- ── ConversationContexts: valor más largo + índice único ───────────────────
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ConversationContexts') AND name = 'Value' AND max_length < 4000)
    ALTER TABLE dbo.ConversationContexts ALTER COLUMN [Value] NVARCHAR(2000) NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConversationContexts_ConversationId_Field' AND object_id = OBJECT_ID('dbo.ConversationContexts'))
    CREATE UNIQUE INDEX [IX_ConversationContexts_ConversationId_Field]
        ON dbo.ConversationContexts ([ConversationId], [Field]);

-- ── Reservations: agregado de ciclo de vida ──────────────────────────────────
IF COL_LENGTH('dbo.Reservations', 'CustomerNameSnapshot') IS NULL
    ALTER TABLE dbo.Reservations ADD [CustomerNameSnapshot] NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.Reservations', 'CustomerEmailSnapshot') IS NULL
    ALTER TABLE dbo.Reservations ADD [CustomerEmailSnapshot] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Reservations', 'CustomerPhoneSnapshot') IS NULL
    ALTER TABLE dbo.Reservations ADD [CustomerPhoneSnapshot] NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.Reservations', 'AvailableSlotsCsv') IS NULL
    ALTER TABLE dbo.Reservations ADD [AvailableSlotsCsv] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.Reservations', 'CustomerConfirmed') IS NULL
    ALTER TABLE dbo.Reservations ADD [CustomerConfirmed] BIT NOT NULL CONSTRAINT DF_Reservations_CustomerConfirmed DEFAULT 0;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Reservations') AND name = 'EmployeeId' AND is_nullable = 0)
    ALTER TABLE dbo.Reservations ALTER COLUMN [EmployeeId] UNIQUEIDENTIFIER NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Reservations') AND name = 'ServiceId' AND is_nullable = 0)
    ALTER TABLE dbo.Reservations ALTER COLUMN [ServiceId] UNIQUEIDENTIFIER NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Reservations') AND name = 'ReservationDateTime' AND is_nullable = 0)
    ALTER TABLE dbo.Reservations ALTER COLUMN [ReservationDateTime] DATETIME2 NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Reservations') AND name = 'DurationMinutes' AND is_nullable = 0)
    ALTER TABLE dbo.Reservations ALTER COLUMN [DurationMinutes] INT NULL;

-- ── PaymentTransactions: completar vertical pago ─────────────────────────────
IF COL_LENGTH('dbo.PaymentTransactions', 'LinkUrl') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [LinkUrl] NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.PaymentTransactions', 'ExpiresAt') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD [ExpiresAt] DATETIME2 NULL;
IF COL_LENGTH('dbo.PaymentTransactions', 'ReservationId') IS NULL
BEGIN
    ALTER TABLE dbo.PaymentTransactions ADD [ReservationId] UNIQUEIDENTIFIER NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PaymentTransactions_Reservations')
        ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT [FK_PaymentTransactions_Reservations]
            FOREIGN KEY ([ReservationId]) REFERENCES dbo.Reservations ([ReservationId]);
END

PRINT N'MigrateMultitenantStateArchitecture: completed.';
GO
