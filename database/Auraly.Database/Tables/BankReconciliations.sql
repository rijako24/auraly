CREATE TABLE [accounting].[BankReconciliations]
(
    [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [BankAccountId] UNIQUEIDENTIFIER NOT NULL,
    [PeriodFrom] DATE NOT NULL,
    [PeriodTo] DATE NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL CONSTRAINT [DF_BankReconciliations_Currency] DEFAULT(N'COP'),
    [OpeningBalance] DECIMAL(19,4) NOT NULL,
    [ClosingBalance] DECIMAL(19,4) NOT NULL,
    [FileName] NVARCHAR(260) NOT NULL,
    [FileSha256] BINARY(32) NOT NULL,
    [OriginalFile] VARBINARY(MAX) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_BankReconciliations_Status] DEFAULT(N'Preparation'),
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ClosedByUserId] UNIQUEIDENTIFIER NULL,
    [ClosedAt] DATETIMEOFFSET(7) NULL,
    [ReopenedByUserId] UNIQUEIDENTIFIER NULL,
    [ReopenedAt] DATETIMEOFFSET(7) NULL,
    [ReopenReason] NVARCHAR(500) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_BankReconciliations] PRIMARY KEY CLUSTERED ([ReconciliationId]),
    CONSTRAINT [FK_BankReconciliations_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_BankReconciliations_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_BankReconciliations_Bank] FOREIGN KEY ([BankAccountId]) REFERENCES [accounting].[BankAccounts]([BankAccountId]),
    CONSTRAINT [FK_BankReconciliations_CreatedBy] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_BankReconciliations_UpdatedBy] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_BankReconciliations_ClosedBy] FOREIGN KEY ([ClosedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_BankReconciliations_ReopenedBy] FOREIGN KEY ([ReopenedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_BankReconciliations_Period] CHECK ([PeriodFrom]<=[PeriodTo]),
    CONSTRAINT [CK_BankReconciliations_Currency] CHECK ([CurrencyCode]=N'COP'),
    CONSTRAINT [CK_BankReconciliations_Status] CHECK ([Status] IN(N'Preparation',N'Closed',N'Reopened')),
    CONSTRAINT [CK_BankReconciliations_File] CHECK (LEN(LTRIM(RTRIM([FileName])))>0 AND DATALENGTH([OriginalFile])>0)
);
GO
CREATE UNIQUE INDEX [UX_BankReconciliations_Tenant_Bank_Period]
    ON [accounting].[BankReconciliations]([TenantId],[BankAccountId],[PeriodFrom],[PeriodTo]);
GO
CREATE INDEX [IX_BankReconciliations_Tenant_Business]
    ON [accounting].[BankReconciliations]([TenantId],[BusinessId],[PeriodTo] DESC)
    INCLUDE([BankAccountId],[Status],[ClosingBalance]);
GO

CREATE TABLE [accounting].[BankStatementLines]
(
    [StatementLineId] UNIQUEIDENTIFIER NOT NULL,
    [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [TransactionDate] DATE NOT NULL,
    [Description] NVARCHAR(500) NOT NULL,
    [Reference] NVARCHAR(160) NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [Balance] DECIMAL(19,4) NULL,
    [Fingerprint] BINARY(32) NOT NULL,
    CONSTRAINT [PK_BankStatementLines] PRIMARY KEY CLUSTERED ([StatementLineId]),
    CONSTRAINT [FK_BankStatementLines_Reconciliation] FOREIGN KEY ([ReconciliationId]) REFERENCES [accounting].[BankReconciliations]([ReconciliationId]),
    CONSTRAINT [UQ_BankStatementLines_Reconciliation_Line] UNIQUE ([ReconciliationId],[StatementLineId]),
    CONSTRAINT [UQ_BankStatementLines_Number] UNIQUE ([ReconciliationId],[LineNumber]),
    CONSTRAINT [UQ_BankStatementLines_Fingerprint] UNIQUE ([ReconciliationId],[Fingerprint]),
    CONSTRAINT [CK_BankStatementLines_Amount] CHECK ([LineNumber]>0 AND [Amount]<>0 AND LEN(LTRIM(RTRIM([Description])))>0)
);
GO

CREATE TABLE [accounting].[BankReconciliationAllocations]
(
    [MatchId] UNIQUEIDENTIFIER NOT NULL,
    [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [StatementLineId] UNIQUEIDENTIFIER NOT NULL,
    [EntryId] UNIQUEIDENTIFIER NOT NULL,
    [EntryLineNumber] INT NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ReversedByUserId] UNIQUEIDENTIFIER NULL,
    [ReversedAt] DATETIMEOFFSET(7) NULL,
    [ReversalReason] NVARCHAR(500) NULL,
    CONSTRAINT [PK_BankReconciliationAllocations] PRIMARY KEY CLUSTERED ([MatchId]),
    CONSTRAINT [FK_BankReconciliationAllocations_Reconciliation] FOREIGN KEY ([ReconciliationId]) REFERENCES [accounting].[BankReconciliations]([ReconciliationId]),
    CONSTRAINT [FK_BankReconciliationAllocations_Statement] FOREIGN KEY ([ReconciliationId],[StatementLineId]) REFERENCES [accounting].[BankStatementLines]([ReconciliationId],[StatementLineId]),
    CONSTRAINT [FK_BankReconciliationAllocations_EntryLine] FOREIGN KEY ([EntryId],[EntryLineNumber]) REFERENCES [dbo].[AccountingEntryLines]([EntryId],[LineNumber]),
    CONSTRAINT [FK_BankReconciliationAllocations_CreatedBy] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [FK_BankReconciliationAllocations_ReversedBy] FOREIGN KEY ([ReversedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_BankReconciliationAllocations_Amount] CHECK ([Amount]>0)
);
GO
CREATE INDEX [IX_BankReconciliationAllocations_Statement]
    ON [accounting].[BankReconciliationAllocations]([StatementLineId],[ReversedAt]) INCLUDE([Amount],[EntryId],[EntryLineNumber]);
GO
CREATE INDEX [IX_BankReconciliationAllocations_Entry]
    ON [accounting].[BankReconciliationAllocations]([EntryId],[EntryLineNumber],[ReversedAt]) INCLUDE([Amount],[StatementLineId]);
GO

CREATE TABLE [accounting].[BankReconciliationStatusEvents]
(
    [StatusEventId] UNIQUEIDENTIFIER NOT NULL,
    [ReconciliationId] UNIQUEIDENTIFIER NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [Reason] NVARCHAR(500) NULL,
    [ActorUserId] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [StatementClosingBalance] DECIMAL(19,4) NULL,
    [BookClosingBalance] DECIMAL(19,4) NULL,
    [AccountingCutoffPostedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_BankReconciliationStatusEvents] PRIMARY KEY CLUSTERED ([StatusEventId]),
    CONSTRAINT [FK_BankReconciliationStatusEvents_Reconciliation] FOREIGN KEY ([ReconciliationId]) REFERENCES [accounting].[BankReconciliations]([ReconciliationId]),
    CONSTRAINT [FK_BankReconciliationStatusEvents_Actor] FOREIGN KEY ([ActorUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_BankReconciliationStatusEvents_Status] CHECK ([Status] IN(N'Closed',N'Reopened')),
    CONSTRAINT [CK_BankReconciliationStatusEvents_CloseSnapshot] CHECK (
        ([Status]=N'Closed' AND [StatementClosingBalance] IS NOT NULL AND [BookClosingBalance] IS NOT NULL)
        OR ([Status]=N'Reopened' AND [StatementClosingBalance] IS NULL AND [BookClosingBalance] IS NULL))
);
GO
CREATE INDEX [IX_BankReconciliationStatusEvents_Reconciliation]
    ON [accounting].[BankReconciliationStatusEvents]([ReconciliationId],[OccurredAt],[StatusEventId]);
GO
