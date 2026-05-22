-- =============================================================================
-- MigrateConversationVerifications.sql
-- Ledger de hechos verificados para Capability Gate del motor agentic. Idempotente.
-- =============================================================================

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ConversationVerifications', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ConversationVerifications] (
        [VerificationId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_ConversationVerifications] PRIMARY KEY,
        [ConversationId] UNIQUEIDENTIFIER NOT NULL,
        [BusinessId]     UNIQUEIDENTIFIER NOT NULL,
        [FactType]       NVARCHAR(64)     NOT NULL,
        [ScopeKey]       NVARCHAR(256)    NOT NULL,
        [PayloadJson]    NVARCHAR(MAX)    NULL,
        [VerifiedAt]     DATETIME2(3)     NOT NULL,
        [ExpiresAt]      DATETIME2(3)     NULL,
        CONSTRAINT [FK_ConversationVerifications_Conversations]
            FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[Conversations]([ConversationId]),
        CONSTRAINT [FK_ConversationVerifications_Businesses]
            FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId])
    );

    CREATE NONCLUSTERED INDEX [IX_ConversationVerifications_Lookup]
        ON [dbo].[ConversationVerifications] ([ConversationId], [FactType], [ScopeKey], [ExpiresAt]);

    PRINT N'MigrateConversationVerifications: table created.';
END
ELSE
    PRINT N'MigrateConversationVerifications: table already exists — skipped.';
GO
