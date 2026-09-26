CREATE TABLE [dbo].[FiscalSaleCorrections]
(
    [CorrectionId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [RetainedDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [ReasonCode] NVARCHAR(32) NOT NULL,
    [IssuedAt] DATETIMEOFFSET(7) NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [FiscalStatus] NVARCHAR(48) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_FiscalSaleCorrections] PRIMARY KEY CLUSTERED ([CorrectionId]),
    CONSTRAINT [UQ_FiscalSaleCorrections_Original] UNIQUE ([OriginalDocumentId]),
    CONSTRAINT [FK_FiscalSaleCorrections_FiscalDocument]
        FOREIGN KEY ([CorrectionId]) REFERENCES [dbo].[FiscalDocuments] ([DocumentId]),
    CONSTRAINT [FK_FiscalSaleCorrections_Business]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_FiscalSaleCorrections_Original]
        FOREIGN KEY ([OriginalDocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_FiscalSaleCorrections_Retained]
        FOREIGN KEY ([RetainedDocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_FiscalSaleCorrections_User]
        FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [FK_FiscalSaleCorrections_Series]
        FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [CK_FiscalSaleCorrections_Reason]
        CHECK ([ReasonCode]=N'DuplicateInvoice'),
    CONSTRAINT [CK_FiscalSaleCorrections_Documents]
        CHECK ([OriginalDocumentId]<>[RetainedDocumentId]),
    CONSTRAINT [CK_FiscalSaleCorrections_Consecutive]
        CHECK ([DocumentConsecutive]>0)
);
GO
CREATE INDEX [IX_FiscalSaleCorrections_Business_Issued]
    ON [dbo].[FiscalSaleCorrections] ([BusinessId],[IssuedAt])
    INCLUDE ([OriginalDocumentId],[RetainedDocumentId],[FiscalStatus]);
GO
