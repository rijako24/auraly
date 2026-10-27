CREATE TABLE [accounting].[VoucherDrafts]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Tenants]([TenantId]),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Businesses]([BusinessId]),
    [DocumentType] NVARCHAR(64) NOT NULL,
    CONSTRAINT [CK_VoucherDrafts_PayloadVersion] CHECK ([PayloadVersion] IS NULL OR
      ([DocumentType]=N'ManualAccountingVoucher' AND [PayloadVersion] IN(2,4))),
    CONSTRAINT [FK_VoucherDrafts_VisibleType] FOREIGN KEY ([VoucherTypeCatalog],[VoucherTypeCode])
      REFERENCES [reference].[Options]([CatalogCode],[Code]),
    CONSTRAINT [CK_VoucherDrafts_TypeCatalog] CHECK ([VoucherTypeCatalog]=N'accounting-voucher-type'),
    CONSTRAINT [CK_VoucherDrafts_FinancialCapture] CHECK (CASE WHEN [PayloadVersion]=4
      THEN CASE WHEN [VoucherTypeCode] IS NOT NULL THEN 1 ELSE 0 END
      ELSE CASE WHEN [VoucherTypeCode] IS NULL AND [WorkSessionId] IS NULL THEN 1 ELSE 0 END END=1),
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ConceptCode] NVARCHAR(40) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL,
    [Reference] NVARCHAR(100) NULL,
    CONSTRAINT [CK_VoucherDrafts_CloneSource] CHECK ([ClonedFromDocumentId] IS NULL OR [ClonedFromDocumentId]<>[DocumentId]),
    [CurrencyCode] NVARCHAR(3) NOT NULL,
    [SubledgerKind] NVARCHAR(16) NULL,
    [SubledgerId] UNIQUEIDENTIFIER NULL,
    [Direction] NVARCHAR(16) NULL,
    [AdjustmentAmount] DECIMAL(19,4) NULL,
    [CounterpartAccountId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    [CostCenterId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingCostCenters]([CostCenterId]),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[AppUsers]([UserId]),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[AppUsers]([UserId]),
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [SentBy] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AppUsers]([UserId]),
    [SentAt] DATETIMEOFFSET(7) NULL,
    [ContentHash] BINARY(32) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [PayloadVersion] TINYINT NULL,
    [VoucherTypeCode] NVARCHAR(64) NULL,
    [VoucherTypeCatalog] NVARCHAR(64) NOT NULL CONSTRAINT [DF_VoucherDrafts_TypeCatalog] DEFAULT N'accounting-voucher-type',
    [WorkSessionId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[WorkSessions]([WorkSessionId]),
    [FiscalCaptureJson] NVARCHAR(MAX) NULL,
    CONSTRAINT [CK_VoucherDrafts_FiscalCapture] CHECK ([FiscalCaptureJson] IS NULL OR
      (ISJSON([FiscalCaptureJson])=1 AND [PayloadVersion] IS NOT NULL AND [PayloadVersion]=4
        AND [DocumentType]=N'ManualAccountingVoucher' AND [VoucherTypeCode] IS NOT NULL
        AND [VoucherTypeCode] IN(N'SupportDocument',N'SupportDocumentCancellation'))),
    [MainPartyId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[Parties]([PartyId]),
    [ClonedFromDocumentId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [CK_VoucherDrafts_Type] CHECK ([DocumentType] IN (N'ManualAccountingVoucher',N'AccountAdjustment')),
    CONSTRAINT [CK_VoucherDrafts_Adjustment] CHECK (
      ([DocumentType]=N'ManualAccountingVoucher' AND [SubledgerId] IS NULL AND [SubledgerKind] IS NULL AND [Direction] IS NULL AND [AdjustmentAmount] IS NULL AND [CounterpartAccountId] IS NULL AND [CostCenterId] IS NULL)
      OR ([DocumentType]=N'AccountAdjustment' AND [SubledgerId] IS NOT NULL AND [SubledgerKind] IS NOT NULL AND [SubledgerKind] IN(N'Receivable',N'Payable') AND [Direction] IS NOT NULL AND [Direction] IN(N'Increase',N'Decrease') AND [AdjustmentAmount] IS NOT NULL AND [AdjustmentAmount]>=0)),
    CONSTRAINT [CK_VoucherDrafts_Sent] CHECK (([SentAt] IS NULL AND [SentBy] IS NULL) OR ([SentAt] IS NOT NULL AND [SentBy] IS NOT NULL))
);
GO
CREATE INDEX [IX_VoucherDrafts_ScopeDate] ON [accounting].[VoucherDrafts]([TenantId],[BusinessId],[OccurredAt],[DocumentId]) INCLUDE([DocumentType],[SentAt]);
GO
CREATE INDEX [IX_VoucherDrafts_MainParty] ON [accounting].[VoucherDrafts]([TenantId],[BusinessId],[MainPartyId],[OccurredAt]) WHERE [MainPartyId] IS NOT NULL;
GO
CREATE INDEX [IX_VoucherDrafts_Session] ON [accounting].[VoucherDrafts]([TenantId],[BusinessId],[WorkSessionId])
    INCLUDE([SentAt]) WHERE [WorkSessionId] IS NOT NULL;
GO
-- Compatibility: deployed payment binaries name this index explicitly. Keep
-- it until all readers use ObligationAvailability and rollback no longer needs
-- those binaries; new reservations never read draft amounts.
CREATE INDEX [IX_VoucherDrafts_Obligation] ON [accounting].[VoucherDrafts]([BusinessId],[SubledgerKind],[SubledgerId]) INCLUDE([Direction],[SentAt],[AdjustmentAmount],[DocumentType]) WHERE [SubledgerId] IS NOT NULL AND [SentAt] IS NOT NULL;
GO
CREATE TABLE [accounting].[VoucherDraftLines]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL REFERENCES [accounting].[VoucherDrafts]([DocumentId]),
    [LineNumber] INT NOT NULL,
    CONSTRAINT [CK_VoucherDraftLines_Identity] CHECK ([LineId] IS NULL OR [LineId]<>'00000000-0000-0000-0000-000000000000'),
    [AccountId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    [PartyId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[Parties]([PartyId]),
    [CostCenterId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingCostCenters]([CostCenterId]),
    [Description] NVARCHAR(500) NOT NULL,
    [Reference] NVARCHAR(100) NULL,
    [Debit] DECIMAL(19,4) NOT NULL,
    [Credit] DECIMAL(19,4) NOT NULL,
    [LineId] UNIQUEIDENTIFIER NULL,
    -- Optional signed document evidence. NULL does not mean a zero base.
    [BaseAmount] DECIMAL(19,4) NULL,
    [TargetSubledgerKind] NVARCHAR(16) NULL,
    [TargetSubledgerId] UNIQUEIDENTIFIER NULL,
    [ReversesTransactionId] UNIQUEIDENTIFIER NULL,
    [FinancialDiscountEarned] BIT NULL,
    [FinancialGroupId] UNIQUEIDENTIFIER NULL,
    [DueDate] DATETIMEOFFSET(7) NULL,
    [PartySiteId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[PartySites]([PartySiteId]),
    [CardFranchiseCode] NVARCHAR(64) NULL,
    [ApprovalNumber] NVARCHAR(100) NULL,
    [TenderedAmount] DECIMAL(19,4) NULL,
    [PaymentNotes] NVARCHAR(500) NULL,
    CONSTRAINT [CK_VoucherDraftLines_Target] CHECK (
      ([TargetSubledgerKind] IS NULL AND [TargetSubledgerId] IS NULL) OR
      ([TargetSubledgerKind] IS NOT NULL AND [TargetSubledgerKind] IN(N'Payable',N'Receivable',N'SupplierCredit',N'CustomerCredit')
        AND [TargetSubledgerId] IS NOT NULL AND [TargetSubledgerId]<>'00000000-0000-0000-0000-000000000000')),
    CONSTRAINT [CK_VoucherDraftLines_Group] CHECK ([FinancialGroupId] IS NULL OR [FinancialGroupId]<>'00000000-0000-0000-0000-000000000000'),
    CONSTRAINT [CK_VoucherDraftLines_Reversal] CHECK ([ReversesTransactionId] IS NULL OR
      ([ReversesTransactionId]<>'00000000-0000-0000-0000-000000000000' AND [TargetSubledgerId] IS NOT NULL
        AND [TargetSubledgerKind] IN(N'Payable',N'Receivable'))),
    CONSTRAINT [CK_VoucherDraftLines_FinancialDiscount] CHECK ([FinancialDiscountEarned] IS NULL OR
      ([TargetSubledgerKind]=N'Payable' AND [TargetSubledgerId] IS NOT NULL
        AND [ReversesTransactionId] IS NULL AND [FinancialGroupId] IS NOT NULL)),
    CONSTRAINT [CK_VoucherDraftLines_Tendered] CHECK ([TenderedAmount] IS NULL OR [TenderedAmount]>0),
    CONSTRAINT [PK_VoucherDraftLines] PRIMARY KEY([DocumentId],[LineNumber]),
    CONSTRAINT [CK_VoucherDraftLines_Values] CHECK ([LineNumber] BETWEEN 1 AND 500 AND [Debit]>=0 AND [Credit]>=0)
);
GO
CREATE INDEX [IX_VoucherDraftLines_Party] ON [accounting].[VoucherDraftLines]([PartyId],[DocumentId]) WHERE [PartyId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_VoucherDraftLines_LineId] ON [accounting].[VoucherDraftLines]([DocumentId],[LineId]) WHERE [LineId] IS NOT NULL;
