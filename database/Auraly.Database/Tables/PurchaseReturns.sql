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
    CONSTRAINT [PK_PurchaseReturnLines] PRIMARY KEY CLUSTERED ([PurchaseReturnId], [LineNumber]),
    CONSTRAINT [FK_PurchaseReturnLines_Return] FOREIGN KEY ([PurchaseReturnId]) REFERENCES [dbo].[PurchaseReturns] ([PurchaseReturnId]),
    CONSTRAINT [FK_PurchaseReturnLines_OriginalLine] FOREIGN KEY ([OriginalGoodsReceiptId], [OriginalLineNumber]) REFERENCES [dbo].[GoodsReceiptLines] ([GoodsReceiptId], [LineNumber]),
    CONSTRAINT [FK_PurchaseReturnLines_Product] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [UQ_PurchaseReturnLines_Original] UNIQUE ([PurchaseReturnId], [OriginalGoodsReceiptId], [OriginalLineNumber]),
    CONSTRAINT [CK_PurchaseReturnLines_Amounts] CHECK ([LineNumber] > 0 AND [OriginalLineNumber] > 0 AND [Quantity] > 0 AND [UnitCost] >= 0 AND [DiscountAmount] >= 0 AND [TaxRate] BETWEEN 0 AND 100 AND [RecognizedUnitCost] >= 0 AND [LineTotal] = [NetAmount] + [TaxAmount])
);
GO
CREATE INDEX [IX_PurchaseReturnLines_OriginalLine]
    ON [dbo].[PurchaseReturnLines] ([OriginalGoodsReceiptId], [OriginalLineNumber])
    INCLUDE ([Quantity], [DiscountAmount], [NetAmount], [TaxAmount], [LineTotal]);
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
    [SourcePurchaseReturnId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalAmount] DECIMAL(19,4) NOT NULL,
    [AvailableAmount] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_SupplierCredits] PRIMARY KEY CLUSTERED ([SupplierCreditId]),
    CONSTRAINT [FK_SupplierCredits_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_SupplierCredits_Supplier] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_SupplierCredits_Return] FOREIGN KEY ([SourcePurchaseReturnId]) REFERENCES [dbo].[PurchaseReturns] ([PurchaseReturnId]),
    CONSTRAINT [UQ_SupplierCredits_Return] UNIQUE ([SourcePurchaseReturnId]),
    CONSTRAINT [CK_SupplierCredits_Amounts] CHECK ([OriginalAmount] > 0 AND [AvailableAmount] BETWEEN 0 AND [OriginalAmount]),
    CONSTRAINT [CK_SupplierCredits_Status] CHECK ([Status] IN (N'Open',N'PartiallyApplied',N'Applied',N'Cancelled'))
);
GO
CREATE INDEX [IX_SupplierCredits_Business_Supplier]
    ON [dbo].[SupplierCredits] ([BusinessId], [SupplierId], [Status]);
GO
