CREATE TABLE [dbo].[GoodsReceiptDrafts]
(
    [GoodsReceiptDraftId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NULL,
    [SupplierId] UNIQUEIDENTIFIER NULL,
    [PurchaseOrderId] UNIQUEIDENTIFIER NULL,
    [PurchaseEvidenceType] NVARCHAR(40) NULL,
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
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_GoodsReceiptDrafts] PRIMARY KEY CLUSTERED ([GoodsReceiptDraftId]),
    CONSTRAINT [FK_GoodsReceiptDrafts_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_GoodsReceiptDrafts_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_GoodsReceiptDrafts_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [FK_GoodsReceiptDrafts_PurchaseOrder] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [purchasing].[PurchaseOrders] ([PurchaseOrderId]),
    CONSTRAINT [CK_GoodsReceiptDrafts_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrandTotal] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_GoodsReceiptDrafts_Payable] CHECK (([CreatesPayable] = 0) OR ([DueDate] IS NOT NULL)),
    CONSTRAINT [CK_GoodsReceiptDrafts_PurchaseEvidenceType] CHECK ([PurchaseEvidenceType] IS NULL OR [PurchaseEvidenceType] IN
      (N'SupplierElectronicInvoice',N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher'))
);
GO
CREATE INDEX [IX_GoodsReceiptDrafts_Business_Updated]
    ON [dbo].[GoodsReceiptDrafts] ([BusinessId], [UpdatedAt] DESC)
    INCLUDE ([SupplierId], [WarehouseId], [GrandTotal]);
GO

CREATE TABLE [dbo].[GoodsReceiptDraftLines]
(
    [GoodsReceiptDraftId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [PurchaseOrderLineId] UNIQUEIDENTIFIER NULL,
    [OverReceiptReason] NVARCHAR(500) NULL,
    [DescriptionSnapshot] NVARCHAR(250) NOT NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [PresentationNameSnapshot] NVARCHAR(80) NOT NULL CONSTRAINT [DF_GoodsReceiptDraftLines_PresentationName] DEFAULT N'Unidad',
    [PresentationQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_GoodsReceiptDraftLines_PresentationQuantity] DEFAULT 1,
    [UnitsPerPresentation] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_GoodsReceiptDraftLines_UnitsPerPresentation] DEFAULT 1,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(32) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [TaxTreatment] NVARCHAR(32) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_GoodsReceiptDraftLines] PRIMARY KEY CLUSTERED ([GoodsReceiptDraftId], [LineNumber]),
    CONSTRAINT [FK_GoodsReceiptDraftLines_Drafts] FOREIGN KEY ([GoodsReceiptDraftId]) REFERENCES [dbo].[GoodsReceiptDrafts] ([GoodsReceiptDraftId]) ON DELETE CASCADE,
    CONSTRAINT [FK_GoodsReceiptDraftLines_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_GoodsReceiptDraftLines_PurchaseOrderLine] FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [purchasing].[PurchaseOrderLines] ([LineId]),
    CONSTRAINT [CK_GoodsReceiptDraftLines_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_GoodsReceiptDraftLines_Costs] CHECK ([UnitCost] >= 0 AND [DiscountAmount] >= 0),
    CONSTRAINT [CK_GoodsReceiptDraftLines_Tax] CHECK ([TaxRate] BETWEEN 0 AND 100),
    CONSTRAINT [CK_GoodsReceiptDraftLines_Treatment] CHECK ([TaxTreatment] IN (N'DeductibleInputVat',N'CapitalizedCost',N'NotApplicable')),
    CONSTRAINT [CK_GoodsReceiptDraftLines_Presentation] CHECK ([PresentationQuantity] > 0 AND [UnitsPerPresentation] > 0 AND [Quantity] = [PresentationQuantity] * [UnitsPerPresentation]),
    CONSTRAINT [CK_GoodsReceiptDraftLines_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] = [NetAmount] + [TaxAmount])
);
GO
CREATE INDEX [IX_GoodsReceiptDraftLines_Product]
    ON [dbo].[GoodsReceiptDraftLines] ([ProductId], [GoodsReceiptDraftId]);
GO
