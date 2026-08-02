CREATE TABLE [dbo].[InventoryOperations]
(
    [InventoryOperationId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(64) NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [DestinationWarehouseId] UNIQUEIDENTIFIER NULL,
    [DocumentSeriesId] UNIQUEIDENTIFIER NULL,
    [DocumentNumber] NVARCHAR(40) NULL,
    [DocumentPrefix] NVARCHAR(8) NULL,
    [DocumentSeriesCode] NVARCHAR(16) NULL,
    [DocumentConsecutive] BIGINT NULL,
    [IdempotencyKey] NVARCHAR(160) NULL,
    [PayloadHash] BINARY(32) NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ReasonCode] NVARCHAR(40) NOT NULL,
    [ConversionType] NVARCHAR(16) NULL,
    [CostCenterId] UNIQUEIDENTIFIER NULL,
    [BaseInventorySequence] BIGINT NULL,
    [Notes] NVARCHAR(1000) NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [ConfirmedByUserId] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [AcceptedAt] DATETIMEOFFSET(7) NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [TotalValueChange] DECIMAL(19,4) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryOperations] PRIMARY KEY CLUSTERED ([InventoryOperationId]),
    CONSTRAINT [FK_InventoryOperations_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_InventoryOperations_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_InventoryOperations_DestinationWarehouse] FOREIGN KEY ([DestinationWarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_InventoryOperations_DocumentSeries] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [dbo].[DocumentSeries] ([DocumentSeriesId]),
    CONSTRAINT [CK_InventoryOperations_Type] CHECK ([DocumentType] IN (N'StockCount',N'InventoryAdjustment',N'WarehouseTransfer',N'ProductConversion')),
    CONSTRAINT [CK_InventoryOperations_Status] CHECK ([Status] IN (N'Draft',N'Accepted',N'Processed')),
    CONSTRAINT [CK_InventoryOperations_Transfer] CHECK (([DocumentType]=N'WarehouseTransfer' AND [DestinationWarehouseId] IS NOT NULL AND [DestinationWarehouseId]<>[WarehouseId]) OR ([DocumentType]<>N'WarehouseTransfer' AND [DestinationWarehouseId] IS NULL)),
    CONSTRAINT [CK_InventoryOperations_CountBase] CHECK (([DocumentType]=N'StockCount' AND [BaseInventorySequence] IS NOT NULL) OR ([DocumentType]<>N'StockCount' AND [BaseInventorySequence] IS NULL)),
    CONSTRAINT [CK_InventoryOperations_AcceptedFields] CHECK (([Status]=N'Draft' AND [DocumentSeriesId] IS NULL AND [DocumentNumber] IS NULL AND [IdempotencyKey] IS NULL) OR ([Status]<>N'Draft' AND [DocumentSeriesId] IS NOT NULL AND [DocumentNumber] IS NOT NULL AND [IdempotencyKey] IS NOT NULL))
);
GO
CREATE UNIQUE INDEX [UX_InventoryOperations_Business_Number]
    ON [dbo].[InventoryOperations]([BusinessId],[DocumentNumber]) WHERE [DocumentNumber] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_InventoryOperations_Business_Idempotency]
    ON [dbo].[InventoryOperations]([BusinessId],[IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL;
GO
CREATE INDEX [IX_InventoryOperations_Business_Occurred]
    ON [dbo].[InventoryOperations]([BusinessId],[OccurredAt] DESC)
    INCLUDE([DocumentType],[WarehouseId],[DestinationWarehouseId],[Status],[DocumentNumber]);
GO

CREATE TABLE [dbo].[InventoryOperationLines]
(
    [InventoryOperationId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [Direction] NVARCHAR(16) NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCodeSnapshot] NVARCHAR(80) NOT NULL,
    [DescriptionSnapshot] NVARCHAR(250) NOT NULL,
    [Quantity] DECIMAL(19,6) NULL,
    [SystemQuantityAtBase] DECIMAL(19,6) NULL,
    [ExplicitUnitCost] DECIMAL(19,6) NULL,
    [AllocationWeight] DECIMAL(9,6) NULL,
    [ProcessedUnitCost] DECIMAL(19,6) NULL,
    [ProcessedValue] DECIMAL(19,4) NULL,
    CONSTRAINT [PK_InventoryOperationLines] PRIMARY KEY CLUSTERED ([InventoryOperationId],[LineNumber]),
    CONSTRAINT [FK_InventoryOperationLines_Operation] FOREIGN KEY ([InventoryOperationId]) REFERENCES [dbo].[InventoryOperations] ([InventoryOperationId]),
    CONSTRAINT [FK_InventoryOperationLines_Product] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_InventoryOperationLines_Line] CHECK ([LineNumber]>0),
    CONSTRAINT [CK_InventoryOperationLines_Direction] CHECK ([Direction] IN (N'COUNT',N'ADJUSTMENT',N'TRANSFER',N'INPUT',N'OUTPUT')),
    CONSTRAINT [CK_InventoryOperationLines_Cost] CHECK ([ExplicitUnitCost] IS NULL OR [ExplicitUnitCost]>=0),
    CONSTRAINT [CK_InventoryOperationLines_Weight] CHECK ([AllocationWeight] IS NULL OR [AllocationWeight]>0)
);
GO
CREATE INDEX [IX_InventoryOperationLines_Product]
    ON [dbo].[InventoryOperationLines]([ProductId],[InventoryOperationId]);
