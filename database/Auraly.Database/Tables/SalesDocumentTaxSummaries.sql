CREATE TABLE [dbo].[SalesDocumentTaxSummaries]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [TaxCode] NVARCHAR(16) NOT NULL,
    [TaxRate] DECIMAL(9, 6) NOT NULL,
    [TaxableAmount] DECIMAL(19, 4) NOT NULL,
    [TaxAmount] DECIMAL(19, 4) NOT NULL,
    [TotalAmount] DECIMAL(19, 4) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SalesDocumentTaxSummaries]
        PRIMARY KEY CLUSTERED ([DocumentId], [TaxCode], [TaxRate]),
    CONSTRAINT [FK_SalesDocumentTaxSummaries_SalesDocuments]
        FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [CK_SalesDocumentTaxSummaries_Values]
        CHECK ([TaxRate] >= 0 AND [TaxableAmount] >= 0 AND [TaxAmount] >= 0 AND [TotalAmount] >= 0)
);

GO

CREATE INDEX [IX_SalesDocumentTaxSummaries_TaxCode_Rate_Document]
    ON [dbo].[SalesDocumentTaxSummaries] ([TaxCode], [TaxRate], [DocumentId]);