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
    -- SQL Server persists TRY_CONVERT without a style as TRY_CAST in computed
    -- definitions. Match that representation so DacFx replay does not propose
    -- dropping/recreating these projections and their indexes on every release.
    [VoucherTypeCode] AS (CONVERT(NVARCHAR(64),CASE
      WHEN [SourceDocumentType]=N'ManualAccountingVoucher' AND ISJSON([PayloadJson])=1
        AND TRY_CAST(JSON_VALUE([PayloadJson],'$.PayloadVersion') AS INT)=4
      THEN JSON_VALUE([PayloadJson],'$.VoucherTypeCode') END)) PERSISTED,
    -- A posted journal reversal remains a separate immutable source. The
    -- filtered unique key prevents two reversals of the same original.
    [ReversesDocumentId] UNIQUEIDENTIFIER NULL,
    -- Derived by the same financial acceptance from an individual sale
    -- correction. These are lookup/idempotency links, never another balance.
    [CorrectsSaleDocumentId] UNIQUEIDENTIFIER NULL,
    [PaymentCorrectionVerificationKey] NVARCHAR(200) NULL,
    [PaymentCorrectionClosureId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [CK_AccountingSourceDocuments_SaleCorrection] CHECK (
      ([CorrectsSaleDocumentId] IS NULL AND [PaymentCorrectionVerificationKey] IS NULL
        AND [PaymentCorrectionClosureId] IS NULL)
      OR ([SourceDocumentType]=N'WorkSessionClosureReconciliation'
        AND [CorrectsSaleDocumentId] IS NOT NULL AND [CorrectsSaleDocumentId]<>[SourceDocumentId]
        AND [PaymentCorrectionVerificationKey] IS NOT NULL AND LEN([PaymentCorrectionVerificationKey])>0
        AND [PaymentCorrectionClosureId] IS NOT NULL AND ISJSON([PayloadJson])=1
        AND JSON_QUERY([PayloadJson],N'$.paymentCorrections[1]') IS NULL
        AND JSON_VALUE([PayloadJson],N'$.paymentCorrections[0].verificationKey') IS NOT NULL
        AND JSON_VALUE([PayloadJson],N'$.paymentCorrections[0].verificationKey')=[PaymentCorrectionVerificationKey]
        AND TRY_CAST(JSON_VALUE([PayloadJson],N'$.paymentCorrections[0].sourceId') AS uniqueidentifier) IS NOT NULL
        AND TRY_CAST(JSON_VALUE([PayloadJson],N'$.paymentCorrections[0].sourceId') AS uniqueidentifier)=[CorrectsSaleDocumentId])),
    CONSTRAINT [FK_AccountingSourceDocuments_CorrectedSale] FOREIGN KEY ([CorrectsSaleDocumentId])
      REFERENCES [dbo].[SalesDocuments]([DocumentId]),
    CONSTRAINT [FK_AccountingSourceDocuments_CorrectionClosure] FOREIGN KEY ([PaymentCorrectionClosureId])
      REFERENCES [dbo].[WorkSessionClosures]([WorkSessionClosureId]),
    CONSTRAINT [CK_AccountingSourceDocuments_ManualReversal] CHECK ([ReversesDocumentId] IS NULL OR
      ([SourceDocumentType]=N'ManualAccountingVoucher' AND [ReversesDocumentId]<>[SourceDocumentId])),
    -- Derived from the immutable request, including legacy direct adjustments
    -- that have no VoucherDraft. This is a lookup key, never another balance.
    [AdjustmentObligationId] AS (
        CASE WHEN [SourceDocumentType]=N'AccountAdjustment' AND ISJSON([PayloadJson])=1
        THEN TRY_CAST(JSON_VALUE([PayloadJson],'$.SubledgerId') AS UNIQUEIDENTIFIER) END
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
CREATE UNIQUE INDEX [UX_AccountingSourceDocuments_ManualReversal]
    ON [dbo].[AccountingSourceDocuments]([TenantId],[BusinessId],[ReversesDocumentId])
    WHERE [ReversesDocumentId] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_AccountingSourceDocuments_SalePaymentCorrection]
    ON [dbo].[AccountingSourceDocuments]([TenantId],[BusinessId],[CorrectsSaleDocumentId],[PaymentCorrectionVerificationKey])
    INCLUDE([AccountingPostingJobId],[PaymentCorrectionClosureId])
    WHERE [CorrectsSaleDocumentId] IS NOT NULL;
GO

CREATE INDEX [IX_AccountingSourceDocuments_CorrectionClosure]
    ON [dbo].[AccountingSourceDocuments]([TenantId],[BusinessId],[PaymentCorrectionClosureId])
    INCLUDE([CorrectsSaleDocumentId],[PaymentCorrectionVerificationKey],[AccountingPostingJobId])
    WHERE [PaymentCorrectionClosureId] IS NOT NULL;
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
-- candidate of a frequently used third party or voucher-type filter.
CREATE INDEX [IX_AccountingSourceDocuments_Manual_PartyProjection]
    ON [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType])
    INCLUDE([TenantId],[BusinessId],[OccurredAt],[AccountingPostingJobId],[VoucherTypeCode])
    WHERE [SourceDocumentType]=N'ManualAccountingVoucher';
GO

CREATE INDEX [IX_AccountingSourceDocuments_UnlinkedJob_Scope_Date]
    ON [dbo].[AccountingSourceDocuments]([TenantId],[BusinessId],[OccurredAt])
    -- Include the IS NULL column so SQL Server can use the filtered index
    -- without returning to the wide immutable source (Microsoft KB 3051225).
    INCLUDE([AccountingPostingJobId])
    WHERE [AccountingPostingJobId] IS NULL;
GO
