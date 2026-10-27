-- Derived destinations of accepted non-cash applications. Amount is copied
-- exclusively from the immutable source for indexed reservation reads; it is
-- neither an editable balance nor another financial transaction.
CREATE TABLE [accounting].[SourcePositionIndex]
(
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [EffectId] UNIQUEIDENTIFIER NOT NULL,
    [SubledgerKind] NVARCHAR(16) NOT NULL,
    [SubledgerId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [EffectKind] NVARCHAR(24) NOT NULL CONSTRAINT [DF_SourcePositionIndex_EffectKind] DEFAULT N'CreditApplication',
    [ReversesTransactionId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_SourcePositionIndex] PRIMARY KEY ([SourceDocumentId],[SourceDocumentType],[EffectId],[SubledgerKind]),
    CONSTRAINT [FK_SourcePositionIndex_Source] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType])
      REFERENCES [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [CK_SourcePositionIndex_Type] CHECK ([SourceDocumentType]=N'ManualAccountingVoucher'),
    CONSTRAINT [CK_SourcePositionIndex_Kind] CHECK ([SubledgerKind] IN(N'Payable',N'Receivable',N'SupplierCredit',N'CustomerCredit')),
    CONSTRAINT [CK_SourcePositionIndex_Amount] CHECK ([Amount]>0),
    CONSTRAINT [CK_SourcePositionIndex_Effect] CHECK (
      ([EffectKind]=N'CreditApplication' AND [ReversesTransactionId] IS NULL) OR
      ([EffectKind]=N'Cancellation' AND [SubledgerKind]=N'Payable' AND [ReversesTransactionId] IS NULL) OR
      ([EffectKind]=N'Unapplication' AND [SubledgerKind] IN(N'Payable',N'Receivable')
        AND [ReversesTransactionId] IS NOT NULL AND [ReversesTransactionId]<>'00000000-0000-0000-0000-000000000000') OR
      ([EffectKind]=N'CreditUnapplication'
        AND [ReversesTransactionId] IS NOT NULL AND [ReversesTransactionId]<>'00000000-0000-0000-0000-000000000000')),
    CONSTRAINT [CK_SourcePositionIndex_Identities] CHECK ([EffectId]<>'00000000-0000-0000-0000-000000000000'
      AND [SubledgerId]<>'00000000-0000-0000-0000-000000000000' AND [SourceLineId]<>'00000000-0000-0000-0000-000000000000')
);
GO
CREATE INDEX [IX_SourcePositionIndex_Position] ON [accounting].[SourcePositionIndex]([BusinessId],[SubledgerKind],[SubledgerId])
  INCLUDE([EffectId],[SourceDocumentId],[SourceDocumentType],[Amount],[EffectKind]);
GO
CREATE INDEX [IX_SourcePositionIndex_Reversal] ON [accounting].[SourcePositionIndex]([BusinessId],[SubledgerKind],[ReversesTransactionId])
  INCLUDE([SubledgerId],[Amount]) WHERE [ReversesTransactionId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_SourcePositionIndex_Cancellation] ON [accounting].[SourcePositionIndex]
  ([BusinessId],[SubledgerKind],[SubledgerId]) WHERE [EffectKind]=N'Cancellation';
GO
