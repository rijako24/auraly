CREATE TABLE [purchasing].[PurchaseOrderDrafts]
(
    [PurchaseOrderId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NULL,
    [SupplierId] UNIQUEIDENTIFIER NULL,
    [OrderedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpectedAt] DATETIMEOFFSET(7) NULL,
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
    CONSTRAINT [FK_PurchaseOrderDrafts_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PurchaseOrderDrafts_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses]([WarehouseId]),
    CONSTRAINT [FK_PurchaseOrderDrafts_Supplier] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers]([SupplierId]),
    CONSTRAINT [CK_PurchaseOrderDrafts_Amounts] CHECK ([NetAmount]>=0 AND [TaxAmount]>=0 AND [GrandTotal]=[NetAmount]+[TaxAmount]),
    CONSTRAINT [CK_PurchaseOrderDrafts_Dates] CHECK ([ExpectedAt] IS NULL OR [ExpectedAt]>=[OrderedAt])
);
GO
CREATE INDEX [IX_PurchaseOrderDrafts_Business_Updated] ON [purchasing].[PurchaseOrderDrafts]([BusinessId],[UpdatedAt] DESC);
GO

CREATE TABLE [purchasing].[PurchaseOrderDraftLines]
(
    [PurchaseOrderId] UNIQUEIDENTIFIER NOT NULL,
    [LineId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [DescriptionSnapshot] NVARCHAR(250) NOT NULL,
    [OrderedQuantity] DECIMAL(19,6) NOT NULL,
    [PresentationNameSnapshot] NVARCHAR(80) NOT NULL,
    [PresentationQuantity] DECIMAL(19,6) NOT NULL,
    [UnitsPerPresentation] DECIMAL(19,6) NOT NULL,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(32) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [TaxTreatment] NVARCHAR(32) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_PurchaseOrderDraftLines] PRIMARY KEY([PurchaseOrderId],[LineId]),
    CONSTRAINT [UQ_PurchaseOrderDraftLines_Number] UNIQUE([PurchaseOrderId],[LineNumber]),
    CONSTRAINT [FK_PurchaseOrderDraftLines_Draft] FOREIGN KEY([PurchaseOrderId]) REFERENCES [purchasing].[PurchaseOrderDrafts]([PurchaseOrderId]) ON DELETE CASCADE,
    CONSTRAINT [FK_PurchaseOrderDraftLines_Product] FOREIGN KEY([ProductId]) REFERENCES [dbo].[Products]([ProductId]),
    CONSTRAINT [CK_PurchaseOrderDraftLines_Values] CHECK ([LineNumber]>0 AND [OrderedQuantity]>0 AND [PresentationQuantity]>0 AND [UnitsPerPresentation]>0 AND [OrderedQuantity]=[PresentationQuantity]*[UnitsPerPresentation] AND [UnitCost]>=0 AND [DiscountAmount]>=0 AND [TaxRate] BETWEEN 0 AND 100 AND [LineTotal]=[NetAmount]+[TaxAmount])
);
GO

CREATE TABLE [purchasing].[PurchaseOrders]
(
    [PurchaseOrderId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
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
    [OrderedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpectedAt] DATETIMEOFFSET(7) NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [GrandTotal] DECIMAL(19,4) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CloseReason] NVARCHAR(500) NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [ConfirmedAt] DATETIMEOFFSET(7) NOT NULL,
    [ClosedByUserId] UNIQUEIDENTIFIER NULL,
    [ClosedAt] DATETIMEOFFSET(7) NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_PurchaseOrders_Business] FOREIGN KEY([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_PurchaseOrders_Warehouse] FOREIGN KEY([WarehouseId]) REFERENCES [dbo].[Warehouses]([WarehouseId]),
    CONSTRAINT [FK_PurchaseOrders_Supplier] FOREIGN KEY([SupplierId]) REFERENCES [dbo].[Suppliers]([SupplierId]),
    CONSTRAINT [FK_PurchaseOrders_Series] FOREIGN KEY([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries]([DocumentSeriesId]),
    CONSTRAINT [UQ_PurchaseOrders_Number] UNIQUE([BusinessId],[DocumentNumber]),
    CONSTRAINT [UQ_PurchaseOrders_Idempotency] UNIQUE([BusinessId],[IdempotencyKey]),
    CONSTRAINT [CK_PurchaseOrders_Status] CHECK([Status] IN (N'Open',N'PartiallyReceived',N'Received',N'Closed',N'Cancelled')),
    CONSTRAINT [CK_PurchaseOrders_Amounts] CHECK([NetAmount]>=0 AND [TaxAmount]>=0 AND [GrandTotal]=[NetAmount]+[TaxAmount]),
    CONSTRAINT [CK_PurchaseOrders_Dates] CHECK([ExpectedAt] IS NULL OR [ExpectedAt]>=[OrderedAt]),
    CONSTRAINT [CK_PurchaseOrders_Close] CHECK(([Status] IN (N'Closed',N'Cancelled') AND [ClosedAt] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [CloseReason] IS NOT NULL) OR ([Status] NOT IN (N'Closed',N'Cancelled') AND [ClosedAt] IS NULL AND [ClosedByUserId] IS NULL AND [CloseReason] IS NULL))
);
GO
CREATE INDEX [IX_PurchaseOrders_Business_Status] ON [purchasing].[PurchaseOrders]([BusinessId],[Status],[OrderedAt] DESC) INCLUDE([SupplierId],[WarehouseId],[ExpectedAt],[GrandTotal]);
GO

CREATE TABLE [purchasing].[PurchaseOrderLines]
(
    [PurchaseOrderId] UNIQUEIDENTIFIER NOT NULL,
    [LineId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [DescriptionSnapshot] NVARCHAR(250) NOT NULL,
    [OrderedQuantity] DECIMAL(19,6) NOT NULL,
    [ReceivedQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_PurchaseOrderLines_Received] DEFAULT 0,
    [CancelledQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_PurchaseOrderLines_Cancelled] DEFAULT 0,
    [PresentationNameSnapshot] NVARCHAR(80) NOT NULL,
    [PresentationQuantity] DECIMAL(19,6) NOT NULL,
    [UnitsPerPresentation] DECIMAL(19,6) NOT NULL,
    [UnitCost] DECIMAL(19,6) NOT NULL,
    [DiscountAmount] DECIMAL(19,4) NOT NULL,
    [TaxCode] NVARCHAR(32) NOT NULL,
    [TaxRate] DECIMAL(9,6) NOT NULL,
    [TaxTreatment] NVARCHAR(32) NOT NULL,
    [NetAmount] DECIMAL(19,4) NOT NULL,
    [TaxAmount] DECIMAL(19,4) NOT NULL,
    [LineTotal] DECIMAL(19,4) NOT NULL,
    CONSTRAINT [PK_PurchaseOrderLines] PRIMARY KEY([PurchaseOrderId],[LineId]),
    CONSTRAINT [UQ_PurchaseOrderLines_Id] UNIQUE([LineId]),
    CONSTRAINT [UQ_PurchaseOrderLines_Number] UNIQUE([PurchaseOrderId],[LineNumber]),
    CONSTRAINT [FK_PurchaseOrderLines_Order] FOREIGN KEY([PurchaseOrderId]) REFERENCES [purchasing].[PurchaseOrders]([PurchaseOrderId]),
    CONSTRAINT [FK_PurchaseOrderLines_Product] FOREIGN KEY([ProductId]) REFERENCES [dbo].[Products]([ProductId]),
    CONSTRAINT [CK_PurchaseOrderLines_Values] CHECK([LineNumber]>0 AND [OrderedQuantity]>0 AND [ReceivedQuantity]>=0 AND [CancelledQuantity]>=0 AND [CancelledQuantity]<=[OrderedQuantity] AND [PresentationQuantity]>0 AND [UnitsPerPresentation]>0 AND [OrderedQuantity]=[PresentationQuantity]*[UnitsPerPresentation] AND [UnitCost]>=0 AND [DiscountAmount]>=0 AND [TaxRate] BETWEEN 0 AND 100 AND [LineTotal]=[NetAmount]+[TaxAmount])
);
GO
CREATE INDEX [IX_PurchaseOrderLines_Product] ON [purchasing].[PurchaseOrderLines]([ProductId],[PurchaseOrderId]) INCLUDE([OrderedQuantity],[ReceivedQuantity],[CancelledQuantity]);
GO
