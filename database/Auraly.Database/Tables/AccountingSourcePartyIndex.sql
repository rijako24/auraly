-- Derived membership index of immutable manual-voucher snapshots. It contains
-- no balances, amounts, independent lifecycle, or user-editable information.
CREATE TABLE [accounting].[SourcePartyIndex]
(
    [PartyId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Parties]([PartyId]),
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    CONSTRAINT [PK_SourcePartyIndex] PRIMARY KEY([PartyId],[SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [FK_SourcePartyIndex_Source] FOREIGN KEY([SourceDocumentId],[SourceDocumentType])
        REFERENCES [dbo].[AccountingSourceDocuments]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [CK_SourcePartyIndex_Type] CHECK ([SourceDocumentType]=N'ManualAccountingVoucher')
);
GO
CREATE INDEX [IX_SourcePartyIndex_Source] ON [accounting].[SourcePartyIndex]([SourceDocumentId],[SourceDocumentType]);
GO
