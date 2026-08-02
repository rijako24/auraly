CREATE TABLE [dbo].[GoodsReceipts]
(
    [GoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(40) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [SupplierInvoiceNumber] NVARCHAR(80) NULL,
    [SupplierInvoiceDate] DATETIMEOFFSET(7) NULL,
    [ReceivedAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatesPayable] BIT NOT NULL,
    [DueDate] DATETIMEOFFSET(7) NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [GrandTotal] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_GoodsReceipts] PRIMARY KEY CLUSTERED ([GoodsReceiptId]),
    CONSTRAINT [FK_GoodsReceipts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_GoodsReceipts_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_GoodsReceipts_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_GoodsReceipts_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [UQ_GoodsReceipts_Business_Number] UNIQUE ([BusinessId], [DocumentNumber]),
    CONSTRAINT [UQ_GoodsReceipts_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [CK_GoodsReceipts_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrandTotal] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_GoodsReceipts_Payable] CHECK (([CreatesPayable] = 0) OR ([DueDate] IS NOT NULL)),
    CONSTRAINT [CK_GoodsReceipts_Status] CHECK ([Status] IN (N'Accepted', N'Processed'))
);
GO
CREATE INDEX [IX_GoodsReceipts_Business_Received]
    ON [dbo].[GoodsReceipts] ([BusinessId], [ReceivedAt] DESC)
    INCLUDE ([SupplierId], [WarehouseId], [Status], [GrandTotal]);
GO
CREATE UNIQUE INDEX [UX_GoodsReceipts_Supplier_Invoice]
    ON [dbo].[GoodsReceipts] ([BusinessId], [SupplierId], [SupplierInvoiceNumber])
    WHERE [SupplierInvoiceNumber] IS NOT NULL;
GO

CREATE TABLE [dbo].[GoodsReceiptLines]
(
    [GoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [DescriptionSnapshot] NVARCHAR(250) NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(32) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [TaxTreatment] NVARCHAR(32) NOT NULL
        CONSTRAINT [DF_GoodsReceiptLines_TaxTreatment] DEFAULT (N'DeductibleInputVat'),
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_GoodsReceiptLines] PRIMARY KEY CLUSTERED ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [FK_GoodsReceiptLines_Receipts] FOREIGN KEY ([GoodsReceiptId]) REFERENCES [dbo].[GoodsReceipts] ([GoodsReceiptId]),
    CONSTRAINT [FK_GoodsReceiptLines_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_GoodsReceiptLines_Amounts] CHECK ([LineNumber] > 0 AND [Quantity] > 0 AND [UnitCost] >= 0 AND [DiscountAmount] >= 0 AND [TaxRate] BETWEEN 0 AND 100 AND [LineTotal] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_GoodsReceiptLines_TaxTreatment] CHECK ([TaxTreatment] IN (N'DeductibleInputVat', N'CapitalizedCost', N'NotApplicable'))
);
GO
CREATE INDEX [IX_GoodsReceiptLines_Product] ON [dbo].[GoodsReceiptLines] ([ProductId], [GoodsReceiptId]);
GO

CREATE TABLE [dbo].[SupplierCostObservations]
(
    [SupplierCostObservationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [ObservedAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_SupplierCostObservations] PRIMARY KEY CLUSTERED ([SupplierCostObservationId]),
    CONSTRAINT [FK_SupplierCostObservations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierCostObservations_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_SupplierCostObservations_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_SupplierCostObservations_ReceiptLine] FOREIGN KEY ([SourceDocumentId], [SourceLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [UQ_SupplierCostObservations_Source] UNIQUE ([SourceDocumentId], [SourceLineNumber]),
    CONSTRAINT [CK_SupplierCostObservations_Cost] CHECK ([UnitCost] >= 0)
);
GO
CREATE INDEX [IX_SupplierCostObservations_Product]
    ON [dbo].[SupplierCostObservations] ([BusinessId], [SupplierId], [ProductId], [ObservedAt] DESC);
GO

CREATE TABLE [dbo].[SupplierProductLatestCosts]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [PreviousUnitCost] DECIMAL(19,6) NULL,
    [LatestUnitCost] DECIMAL(19,6) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [ObservedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SupplierProductLatestCosts] PRIMARY KEY CLUSTERED ([BusinessId], [SupplierId], [ProductId]),
    CONSTRAINT [FK_SupplierProductLatestCosts_Association] FOREIGN KEY ([BusinessId], [ProductId], [SupplierId]) REFERENCES [dbo].[SupplierProducts] ([BusinessId], [ProductId], [SupplierId]),
    CONSTRAINT [FK_SupplierProductLatestCosts_ReceiptLine] FOREIGN KEY ([SourceDocumentId], [SourceLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [CK_SupplierProductLatestCosts_Cost] CHECK ([LatestUnitCost] >= 0 AND ([PreviousUnitCost] IS NULL OR [PreviousUnitCost] >= 0))
);
GO

CREATE TABLE [dbo].[Payables]
(
    [PayableId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [DocumentNumber] NVARCHAR(40) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [OriginalAmount] DECIMAL(19,4) NOT NULL,
    [OutstandingAmount] DECIMAL(19,4) NOT NULL,
    [DueDate] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Payables] PRIMARY KEY CLUSTERED ([PayableId]),
    CONSTRAINT [FK_Payables_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_Payables_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_Payables_SourceJob] FOREIGN KEY ([SourceDocumentId], [SourceDocumentType]) REFERENCES [dbo].[DocumentProcessingJobs] ([DocumentId], [DocumentType]),
    CONSTRAINT [UQ_Payables_Source] UNIQUE ([SourceDocumentId], [SourceDocumentType]),
    CONSTRAINT [CK_Payables_Amounts] CHECK ([OriginalAmount] > 0 AND [OutstandingAmount] BETWEEN 0 AND [OriginalAmount]),
    CONSTRAINT [CK_Payables_Status] CHECK ([Status] IN (N'Open', N'PartiallyPaid', N'Paid', N'Cancelled'))
);
GO
CREATE INDEX [IX_Payables_Business_Due] ON [dbo].[Payables] ([BusinessId], [Status], [DueDate]);
GO

CREATE TABLE [dbo].[PayableTransactions]
(
    [PayableTransactionId] UNIQUEIDENTIFIER NOT NULL,
    [PayableId] UNIQUEIDENTIFIER NOT NULL,
    [TransactionType] NVARCHAR(24) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PayableTransactions] PRIMARY KEY CLUSTERED ([PayableTransactionId]),
    CONSTRAINT [FK_PayableTransactions_Payables] FOREIGN KEY ([PayableId]) REFERENCES [dbo].[Payables] ([PayableId]),
    CONSTRAINT [UQ_PayableTransactions_Payable_Source_Type] UNIQUE ([PayableId], [SourceDocumentId], [TransactionType]),
    CONSTRAINT [CK_PayableTransactions_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_PayableTransactions_Type] CHECK ([TransactionType] IN (N'Opening', N'Payment', N'Credit', N'Adjustment'))
);
GO

CREATE TABLE [dbo].[PriceRevisionProposals]
(
    [PriceRevisionProposalId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceLineNumber] INT NOT NULL,
    [PreviousObservedUnitCost] DECIMAL(19,6) NULL,
    [ObservedUnitCost] DECIMAL(19,6) NOT NULL,
    [CurrentSalePrice] DECIMAL(19,4) NOT NULL,
    [CurrentMarginPercent] DECIMAL(9,6) NULL,
    [TargetMarginPercent] DECIMAL(9,6) NULL,
    [SuggestedSalePrice] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ReviewedByUserId] UNIQUEIDENTIFIER NULL,
    [ReviewedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PriceRevisionProposals] PRIMARY KEY CLUSTERED ([PriceRevisionProposalId]),
    CONSTRAINT [FK_PriceRevisionProposals_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_PriceRevisionProposals_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_PriceRevisionProposals_ReceiptLine] FOREIGN KEY ([SourceDocumentId], [SourceLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [UQ_PriceRevisionProposals_Source] UNIQUE ([SourceDocumentId], [SourceLineNumber]),
    CONSTRAINT [CK_PriceRevisionProposals_Values] CHECK ([ObservedUnitCost] >= 0 AND [CurrentSalePrice] >= 0 AND [SuggestedSalePrice] >= 0 AND ([CurrentMarginPercent] IS NULL OR [CurrentMarginPercent] < 100) AND ([TargetMarginPercent] IS NULL OR [TargetMarginPercent] BETWEEN 0 AND 99.999999)),
    CONSTRAINT [CK_PriceRevisionProposals_Status] CHECK ([Status] IN (N'PendingReview', N'Approved', N'Published', N'Rejected', N'Superseded'))
);
GO
CREATE INDEX [IX_PriceRevisionProposals_Business_Status]
    ON [dbo].[PriceRevisionProposals] ([BusinessId], [Status], [CreatedAt] DESC)
    INCLUDE ([ProductId], [ObservedUnitCost], [CurrentSalePrice], [SuggestedSalePrice]);
GO
