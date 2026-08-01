CREATE TABLE [dbo].[InventoryMovements]
(
    [InventoryMovementId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(64) NOT NULL CONSTRAINT [DF_InventoryMovements_DocumentType] DEFAULT (N'SalesInvoice'),
    [LineNumber] INT NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [MovementType] NVARCHAR(32) NOT NULL,
    [QuantityChange] DECIMAL(19, 6) NOT NULL,
    [ProcessingSequence] BIGINT NULL,
    [QuantityBefore] DECIMAL(19, 6) NULL,
    [QuantityAfter] DECIMAL(19, 6) NULL,
    [AverageUnitCostBefore] DECIMAL(19, 6) NULL,
    [AverageUnitCostAfter] DECIMAL(19, 6) NULL,
    [RecognizedUnitCost] DECIMAL(19, 6) NULL,
    [ValueChange] DECIMAL(19, 4) NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [PostedAt] DATETIMEOFFSET(7) NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_InventoryMovements] PRIMARY KEY CLUSTERED ([InventoryMovementId]),
    CONSTRAINT [FK_InventoryMovements_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_InventoryMovements_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_InventoryMovements_DocumentJob] FOREIGN KEY ([DocumentId], [DocumentType]) REFERENCES [dbo].[DocumentProcessingJobs] ([DocumentId], [DocumentType]),
    CONSTRAINT [FK_InventoryMovements_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [UQ_InventoryMovements_Document_Line_Type] UNIQUE ([DocumentId], [DocumentType], [LineNumber], [MovementType]),
    CONSTRAINT [CK_InventoryMovements_Quantity] CHECK ([QuantityChange] <> 0)
);

GO

CREATE INDEX [IX_InventoryMovements_Business_Warehouse]
    ON [dbo].[InventoryMovements] ([BusinessId], [WarehouseId], [OccurredAt]);

