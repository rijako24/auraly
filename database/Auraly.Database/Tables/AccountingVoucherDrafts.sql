CREATE TABLE [accounting].[VoucherDrafts]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Tenants]([TenantId]),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Businesses]([BusinessId]),
    [DocumentType] NVARCHAR(64) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ConceptCode] NVARCHAR(40) NOT NULL,
    [Description] NVARCHAR(500) NOT NULL,
    [Reference] NVARCHAR(100) NULL,
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
    CONSTRAINT [CK_VoucherDrafts_Type] CHECK ([DocumentType] IN (N'ManualAccountingVoucher',N'AccountAdjustment')),
    CONSTRAINT [CK_VoucherDrafts_Adjustment] CHECK (
      ([DocumentType]=N'ManualAccountingVoucher' AND [SubledgerId] IS NULL AND [SubledgerKind] IS NULL AND [Direction] IS NULL AND [AdjustmentAmount] IS NULL AND [CounterpartAccountId] IS NULL AND [CostCenterId] IS NULL)
      OR ([DocumentType]=N'AccountAdjustment' AND [SubledgerId] IS NOT NULL AND [SubledgerKind] IS NOT NULL AND [SubledgerKind] IN(N'Receivable',N'Payable') AND [Direction] IS NOT NULL AND [Direction] IN(N'Increase',N'Decrease') AND [AdjustmentAmount] IS NOT NULL AND [AdjustmentAmount]>=0)),
    CONSTRAINT [CK_VoucherDrafts_Sent] CHECK (([SentAt] IS NULL AND [SentBy] IS NULL) OR ([SentAt] IS NOT NULL AND [SentBy] IS NOT NULL))
);
GO
CREATE INDEX [IX_VoucherDrafts_ScopeDate] ON [accounting].[VoucherDrafts]([TenantId],[BusinessId],[OccurredAt],[DocumentId]) INCLUDE([DocumentType],[SentAt]);
GO
CREATE INDEX [IX_VoucherDrafts_Obligation] ON [accounting].[VoucherDrafts]([BusinessId],[SubledgerKind],[SubledgerId]) INCLUDE([Direction],[SentAt],[AdjustmentAmount],[DocumentType]) WHERE [SubledgerId] IS NOT NULL AND [SentAt] IS NOT NULL;
GO
CREATE TABLE [accounting].[VoucherDraftLines]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL REFERENCES [accounting].[VoucherDrafts]([DocumentId]),
    [LineNumber] INT NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingAccounts]([AccountId]),
    [PartyId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[Parties]([PartyId]),
    [CostCenterId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[AccountingCostCenters]([CostCenterId]),
    [Description] NVARCHAR(500) NOT NULL,
    [Reference] NVARCHAR(100) NULL,
    [Debit] DECIMAL(19,4) NOT NULL,
    [Credit] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_VoucherDraftLines] PRIMARY KEY([DocumentId],[LineNumber]),
    CONSTRAINT [CK_VoucherDraftLines_Values] CHECK ([LineNumber] BETWEEN 1 AND 500 AND [Debit]>=0 AND [Credit]>=0)
);
GO
CREATE INDEX [IX_VoucherDraftLines_Party] ON [accounting].[VoucherDraftLines]([PartyId],[DocumentId]) WHERE [PartyId] IS NOT NULL;
