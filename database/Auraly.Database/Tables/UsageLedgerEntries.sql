CREATE TABLE [dbo].[UsageLedgerEntries] (
    [UsageLedgerEntryId]    UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_UsageLedgerEntries] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [BusinessUsagePeriodId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId]            UNIQUEIDENTIFIER NOT NULL,
    [AgentId]               UNIQUEIDENTIFIER NULL,
    [ConversationId]        UNIQUEIDENTIFIER NULL,
    [MessageId]             UNIQUEIDENTIFIER NULL,
    [OperationType]         INT              NOT NULL,
    [CreditsCharged]        INT              NOT NULL,
    [EstimatedCostCop]      DECIMAL(18, 4)   NOT NULL,
    [ActualCostCop]         DECIMAL(18, 4)   NULL,
    [InputTokens]           INT              NOT NULL DEFAULT 0,
    [OutputTokens]          INT              NOT NULL DEFAULT 0,
    [Model]                 NVARCHAR(100)    NOT NULL DEFAULT N'',
    [MetadataJson]          NVARCHAR(MAX)    NULL,
    [CreatedAt]             DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [FK_UsageLedgerEntries_BusinessUsagePeriods] FOREIGN KEY ([BusinessUsagePeriodId])
        REFERENCES [dbo].[BusinessUsagePeriods] ([BusinessUsagePeriodId]) ON DELETE CASCADE,
    CONSTRAINT [FK_UsageLedgerEntries_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_UsageLedgerEntries_Agents] FOREIGN KEY ([AgentId])
        REFERENCES [dbo].[Agents] ([AgentId]) ON DELETE SET NULL,
    CONSTRAINT [FK_UsageLedgerEntries_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId]) ON DELETE SET NULL
);

GO

CREATE INDEX [IX_UsageLedgerEntries_BusinessId] ON [dbo].[UsageLedgerEntries] ([BusinessId]);
GO
CREATE INDEX [IX_UsageLedgerEntries_Period] ON [dbo].[UsageLedgerEntries] ([BusinessUsagePeriodId]);
GO
CREATE INDEX [IX_UsageLedgerEntries_CreatedAt] ON [dbo].[UsageLedgerEntries] ([CreatedAt]);
