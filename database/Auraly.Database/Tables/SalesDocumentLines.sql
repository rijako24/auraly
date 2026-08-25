CREATE TABLE [dbo].[SalesDocumentLines]
(
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Description] NVARCHAR(300) NOT NULL,
    [TaxCode] NVARCHAR(16) NOT NULL,
    [TaxRate] DECIMAL(9, 6) NOT NULL CONSTRAINT [DF_SalesDocumentLines_TaxRate] DEFAULT (0),
    [Quantity] DECIMAL(19, 6) NOT NULL,
    [UnitPrice] DECIMAL(19, 4) NOT NULL,
    [UnitCostSnapshot] DECIMAL(19, 6) NULL,
    [DiscountAmount] DECIMAL(19, 4) NOT NULL,
    [TaxAmount] DECIMAL(19, 4) NOT NULL,
    [UntaxedAmount] DECIMAL(19, 4) NOT NULL,
    [LineTotal] DECIMAL(19, 4) NOT NULL,
    CONSTRAINT [PK_SalesDocumentLines] PRIMARY KEY CLUSTERED ([DocumentId], [LineNumber]),
    CONSTRAINT [FK_SalesDocumentLines_SalesDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[SalesDocuments] ([DocumentId]),
    CONSTRAINT [FK_SalesDocumentLines_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_SalesDocumentLines_Values] CHECK
        ([LineNumber] > 0 AND [Quantity] > 0 AND [UnitPrice] >= 0 AND ([UnitCostSnapshot] IS NULL OR [UnitCostSnapshot] >= 0) AND [DiscountAmount] >= 0 AND [TaxAmount] >= 0 AND [TaxRate] >= 0)
);

GO

CREATE INDEX [IX_SalesDocumentLines_Product_Document]
    ON [dbo].[SalesDocumentLines] ([ProductId], [DocumentId]);

