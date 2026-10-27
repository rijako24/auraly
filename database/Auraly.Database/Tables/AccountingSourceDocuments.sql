CREATE TABLE [dbo].[AccountingSourceDocuments]
(
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PayloadJson] NVARCHAR(MAX) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    -- Frozen when the immutable source is accepted. NULL is reserved for
    -- legacy sources whose original accounting mode cannot be proven.
    [AccountingEntryRequired] BIT NULL,
    -- Derived link to existing work, populated only by acceptance, explicit
    -- legacy recovery or the schema backfill. NULL also covers legacy sources.
    [AccountingPostingJobId] UNIQUEIDENTIFIER NULL,
    -- Derived from the immutable request, including legacy direct adjustments
    -- that have no VoucherDraft. This is a lookup key, never another balance.
    [AdjustmentObligationId] AS (
        CASE WHEN [SourceDocumentType]=N'AccountAdjustment' AND ISJSON([PayloadJson])=1
        THEN TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE([PayloadJson],'$.SubledgerId')) END
    ) PERSISTED,
    CONSTRAINT [PK_AccountingSourceDocuments]
        PRIMARY KEY CLUSTERED ([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [FK_AccountingSourceDocuments_Tenants]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
    CONSTRAINT [FK_AccountingSourceDocuments_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_AccountingSourceDocuments_PostingJob] FOREIGN KEY
      ([TenantId],[BusinessId],[SourceDocumentId],[SourceDocumentType],[AccountingPostingJobId])
      REFERENCES [dbo].[AccountingPostingJobs]
      ([TenantId],[BusinessId],[SourceDocumentId],[SourceDocumentType],[AccountingPostingJobId])
);
GO

CREATE INDEX [IX_AccountingSourceDocuments_AdjustmentObligation]
    ON [dbo].[AccountingSourceDocuments]([BusinessId],[AdjustmentObligationId])
    WHERE [SourceDocumentType]=N'AccountAdjustment';
GO

-- Unposted non-manual party references must not scan manual-voucher payloads.
CREATE INDEX [IX_AccountingSourceDocuments_NonManual_Scope_Date]
    ON [dbo].[AccountingSourceDocuments]([TenantId],[BusinessId],[OccurredAt])
    WHERE [SourceDocumentType] <> N'ManualAccountingVoucher';
GO

CREATE INDEX [IX_AccountingSourceDocuments_Business_Date]
    ON [dbo].[AccountingSourceDocuments]
       ([BusinessId],[OccurredAt],[SourceDocumentId]) INCLUDE([TenantId],[AccountingPostingJobId]);
GO

-- Party-filtered traceability joins manual sources by immutable identity. A
-- narrow covering projection avoids fetching the wide JSON source for every
-- candidate of a frequently used third party.
CREATE INDEX [IX_AccountingSourceDocuments_Manual_PartyProjection]
    ON [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType])
    INCLUDE([TenantId],[BusinessId],[OccurredAt],[AccountingPostingJobId])
    WHERE [SourceDocumentType]=N'ManualAccountingVoucher';
GO

CREATE INDEX [IX_AccountingSourceDocuments_UnlinkedJob_Scope_Date]
    ON [dbo].[AccountingSourceDocuments]([TenantId],[BusinessId],[OccurredAt])
    -- Include the IS NULL column so SQL Server can use the filtered index
    -- without returning to the wide immutable source (Microsoft KB 3051225).
    INCLUDE([AccountingPostingJobId])
    WHERE [AccountingPostingJobId] IS NULL;
GO
