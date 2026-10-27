CREATE TABLE [dbo].[PurchaseReturns]
(
    [PurchaseReturnId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalGoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentNumber] NVARCHAR(40) NOT NULL,
    [DocumentPrefix] NVARCHAR(8) NOT NULL,
    [DocumentSeriesCode] NVARCHAR(16) NOT NULL,
    [DocumentConsecutive] BIGINT NOT NULL,
    [IdempotencyKey] NVARCHAR(160) NOT NULL,
    [PayloadHash] BINARY(32) NOT NULL,
    [ReturnedAt] DATETIMEOFFSET(7) NOT NULL,
    [ReasonCode] NVARCHAR(32) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [TotalAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PurchaseReturns] PRIMARY KEY CLUSTERED ([PurchaseReturnId]),
    CONSTRAINT [FK_PurchaseReturns_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_PurchaseReturns_Receipt] FOREIGN KEY ([OriginalGoodsReceiptId]) REFERENCES [dbo].[GoodsReceipts] ([GoodsReceiptId]),
    CONSTRAINT [FK_PurchaseReturns_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_PurchaseReturns_Supplier] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_PurchaseReturns_Series] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [FK_PurchaseReturns_User] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [dbo].[AppUsers] ([UserId]),
    CONSTRAINT [UQ_PurchaseReturns_Business_Number] UNIQUE ([BusinessId], [DocumentNumber]),
    CONSTRAINT [UQ_PurchaseReturns_Business_Idempotency] UNIQUE ([BusinessId], [IdempotencyKey]),
    CONSTRAINT [CK_PurchaseReturns_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [TotalAmount] > 0 AND [TotalAmount] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_PurchaseReturns_Currency] CHECK ([CurrencyCode] = 'COP'),
    CONSTRAINT [CK_PurchaseReturns_Status] CHECK ([Status] IN (N'Accepted',N'Processed'))
);
GO
CREATE INDEX [IX_PurchaseReturns_Original]
    ON [dbo].[PurchaseReturns] ([BusinessId], [OriginalGoodsReceiptId], [ReturnedAt]);
GO

CREATE TABLE [dbo].[PurchaseReturnLines]
(
    [PurchaseReturnId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [OriginalGoodsReceiptId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalLineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [DescriptionSnapshot] NVARCHAR(250) NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(32) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [TaxTreatment] NVARCHAR(32) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    [RecognizedUnitCost] DECIMAL(19,6) NOT NULL,
    [FinancialDiscountAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_PurchaseReturnLines_FinancialDiscountAmount] DEFAULT (0),
    CONSTRAINT [PK_PurchaseReturnLines] PRIMARY KEY CLUSTERED ([PurchaseReturnId], [LineNumber]),
    CONSTRAINT [FK_PurchaseReturnLines_Return] FOREIGN KEY ([PurchaseReturnId]) REFERENCES [dbo].[PurchaseReturns] ([PurchaseReturnId]),
    CONSTRAINT [FK_PurchaseReturnLines_OriginalLine] FOREIGN KEY ([OriginalGoodsReceiptId], [OriginalLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [FK_PurchaseReturnLines_Product] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [UQ_PurchaseReturnLines_Original] UNIQUE ([PurchaseReturnId], [OriginalGoodsReceiptId], [OriginalLineNumber]),
    CONSTRAINT [CK_PurchaseReturnLines_Amounts] CHECK ([LineNumber] > 0 AND [OriginalLineNumber] > 0 AND [Quantity] > 0 AND [UnitCost] >= 0 AND [DiscountAmount] >= 0 AND [TaxRate] BETWEEN 0 AND 100 AND [RecognizedUnitCost] >= 0 AND [FinancialDiscountAmount] >= 0 AND ([FinancialDiscountAmount] = 0 OR [FinancialDiscountAmount] <= [NetAmount]) AND [LineTotal] = [NetAmount] + [TaxAmount])
);
GO
CREATE INDEX [IX_PurchaseReturnLines_OriginalLine]
    ON [dbo].[PurchaseReturnLines] ([OriginalGoodsReceiptId], [OriginalLineNumber])
    INCLUDE ([Quantity], [DiscountAmount], [NetAmount], [TaxAmount], [LineTotal], [FinancialDiscountAmount]);
GO

CREATE TABLE [dbo].[PurchaseReturnFinancialEffects]
(
    [PurchaseReturnId] UNIQUEIDENTIFIER NOT NULL,
    [PayableId] UNIQUEIDENTIFIER NULL,
    [PayableCreditAmount] DECIMAL(19,4) NOT NULL,
    [SupplierCreditAmount] DECIMAL(19,4) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PurchaseReturnFinancialEffects] PRIMARY KEY CLUSTERED ([PurchaseReturnId]),
    CONSTRAINT [FK_PurchaseReturnFinancialEffects_Return] FOREIGN KEY ([PurchaseReturnId]) REFERENCES [dbo].[PurchaseReturns] ([PurchaseReturnId]),
    CONSTRAINT [FK_PurchaseReturnFinancialEffects_Payable] FOREIGN KEY ([PayableId]) REFERENCES [dbo].[Payables] ([PayableId]),
    CONSTRAINT [CK_PurchaseReturnFinancialEffects_Amounts] CHECK ([PayableCreditAmount] >= 0 AND [SupplierCreditAmount] >= 0 AND [PayableCreditAmount] + [SupplierCreditAmount] > 0)
);
GO

CREATE TABLE [dbo].[SupplierCredits]
(
    [SupplierCreditId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [PartySiteId] UNIQUEIDENTIFIER NULL,
    [SourcePurchaseReturnId] UNIQUEIDENTIFIER NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NULL,
    [SourceDocumentType] NVARCHAR(40) NULL,
    [OriginalAmount] DECIMAL(19,4) NOT NULL,
    [AvailableAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    -- NULL preserves an unclassified historical balance until reconciliation.
    [CreditNature] NVARCHAR(16) NULL,
    [CurrencyCode] CHAR(3) NULL,
    [OpeningEntryId] UNIQUEIDENTIFIER NULL,
    [OpeningEntryLineNumber] INT NULL,
    CONSTRAINT [PK_SupplierCredits] PRIMARY KEY CLUSTERED ([SupplierCreditId]),
    CONSTRAINT [FK_SupplierCredits_OpeningEntryLine] FOREIGN KEY ([OpeningEntryId],[OpeningEntryLineNumber]) REFERENCES [dbo].[AccountingEntryLines]([EntryId],[LineNumber]),
    CONSTRAINT [CK_SupplierCredits_OpeningEntryLine] CHECK (([OpeningEntryId] IS NULL AND [OpeningEntryLineNumber] IS NULL)
      OR ([OpeningEntryId] IS NOT NULL AND [OpeningEntryLineNumber] IS NOT NULL)),
    CONSTRAINT [CK_SupplierCredits_Nature] CHECK ([CreditNature] IN(N'Credit',N'Advance')),
    CONSTRAINT [CK_SupplierCredits_SourceLine] CHECK ([SourceLineId] IS NULL OR
      ([SourceLineId]<>'00000000-0000-0000-0000-000000000000' AND [SourceDocumentType] IS NOT NULL
        AND [SourceDocumentType]=N'ManualAccountingVoucher')),
    CONSTRAINT [FK_SupplierCredits_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierCredits_Supplier] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_SupplierCredits_PartySite] FOREIGN KEY ([PartySiteId]) REFERENCES [dbo].[PartySites] ([PartySiteId]),
    CONSTRAINT [FK_SupplierCredits_Return] FOREIGN KEY ([SourcePurchaseReturnId]) REFERENCES [dbo].[PurchaseReturns] ([PurchaseReturnId]),
    CONSTRAINT [CK_SupplierCredits_Source] CHECK (([SourcePurchaseReturnId] IS NOT NULL AND [SourceDocumentId] IS NULL AND [SourceDocumentType] IS NULL) OR ([SourcePurchaseReturnId] IS NULL AND [SourceDocumentId] IS NOT NULL AND NULLIF(LTRIM(RTRIM([SourceDocumentType])),N'') IS NOT NULL)),
    CONSTRAINT [CK_SupplierCredits_Amounts] CHECK ([OriginalAmount] > 0 AND [AvailableAmount] BETWEEN 0 AND [OriginalAmount]),
    CONSTRAINT [CK_SupplierCredits_Status] CHECK ([Status] IN (N'Open',N'PartiallyApplied',N'Applied',N'Cancelled'))
);
GO
CREATE UNIQUE INDEX [UX_SupplierCredits_SourceDocument]
    ON [dbo].[SupplierCredits] ([SourceDocumentId],[SourceDocumentType],[SourceLineId])
    WHERE [SourceDocumentId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_SupplierCredits_PurchaseReturn]
    ON [dbo].[SupplierCredits] ([SourcePurchaseReturnId])
    WHERE [SourcePurchaseReturnId] IS NOT NULL;
GO
CREATE INDEX [IX_SupplierCredits_Business_Supplier]
    ON [dbo].[SupplierCredits] ([BusinessId], [SupplierId], [PartySiteId], [Status]);
GO

CREATE TABLE [dbo].[SupplierCreditTransactions]
(
    [SupplierCreditTransactionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [SupplierCreditId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[SupplierCredits]([SupplierCreditId]),
    [TransactionType] NVARCHAR(24) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [SourceDocumentId] UNIQUEIDENTIFIER NOT NULL,
    [SourceDocumentType] NVARCHAR(64) NOT NULL,
    [SourceLineId] UNIQUEIDENTIFIER NULL,
    [AppliedPositionTransactionId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[PayableTransactions]([PayableTransactionId]),
    [ReversesTransactionId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[SupplierCreditTransactions]([SupplierCreditTransactionId]),
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_SupplierCreditTransactions_SourceJob] FOREIGN KEY ([SourceDocumentId],[SourceDocumentType])
      REFERENCES [dbo].[AccountingPostingJobs]([SourceDocumentId],[SourceDocumentType]),
    CONSTRAINT [UQ_SupplierCreditTransactions_Source] UNIQUE ([SupplierCreditId],[SourceDocumentId],[SourceDocumentType],[SourceLineId],[TransactionType],[AppliedPositionTransactionId]),
    CONSTRAINT [CK_SupplierCreditTransactions_Type] CHECK (([TransactionType]=N'Opening' AND [AppliedPositionTransactionId] IS NULL AND [ReversesTransactionId] IS NULL)
      OR ([TransactionType]=N'Application' AND [AppliedPositionTransactionId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [SourceLineId] IS NOT NULL AND [SourceDocumentType]=N'ManualAccountingVoucher')
      OR ([TransactionType]=N'Unapplication' AND [AppliedPositionTransactionId] IS NOT NULL AND [ReversesTransactionId] IS NOT NULL AND [SourceLineId] IS NOT NULL AND [SourceDocumentType]=N'ManualAccountingVoucher')),
    CONSTRAINT [CK_SupplierCreditTransactions_Amount] CHECK ([Amount]>0),
    CONSTRAINT [CK_SupplierCreditTransactions_Line] CHECK ([SourceLineId] IS NULL OR [SourceLineId]<>'00000000-0000-0000-0000-000000000000')
);
GO
CREATE UNIQUE INDEX [UX_SupplierCreditTransactions_Opening] ON [dbo].[SupplierCreditTransactions]([SupplierCreditId]) WHERE [TransactionType]=N'Opening';
GO
CREATE UNIQUE INDEX [UX_SupplierCreditTransactions_PositionTransaction] ON [dbo].[SupplierCreditTransactions]([AppliedPositionTransactionId]) WHERE [AppliedPositionTransactionId] IS NOT NULL;
GO
CREATE INDEX [IX_SupplierCreditTransactions_Source] ON [dbo].[SupplierCreditTransactions]([SourceDocumentId],[SourceDocumentType]) INCLUDE([SupplierCreditId],[Amount],[SourceLineId]);
GO
