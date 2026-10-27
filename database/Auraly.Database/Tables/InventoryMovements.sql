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
    -- Immutable evidence from the locked valuation, not another current balance.
    -- NULL identifies historical writers whose pool cannot be reconstructed safely.
    [ValuationMode] NVARCHAR(32) NULL,
    [CostPoolScope] NVARCHAR(24) NULL,
    [PoolQuantityBefore] DECIMAL(28,6) NULL,
    [PoolQuantityAfter] DECIMAL(28,6) NULL,
    [PoolInventoryValueBefore] DECIMAL(28,4) NULL,
    [PoolInventoryValueAfter] DECIMAL(28,4) NULL,
    [CostPoolEpochId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_InventoryMovements] PRIMARY KEY CLUSTERED ([InventoryMovementId]),
    CONSTRAINT [FK_InventoryMovements_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_InventoryMovements_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_InventoryMovements_DocumentJob] FOREIGN KEY ([DocumentId], [DocumentType]) REFERENCES [dbo].[DocumentProcessingJobs] ([DocumentId], [DocumentType]),
    CONSTRAINT [FK_InventoryMovements_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_InventoryMovements_CostPoolEpoch] FOREIGN KEY ([CostPoolEpochId],[ProductId],[CostPoolScope])
        REFERENCES [inventory].[CostPoolEpochs]([EpochId],[ProductId],[ScopeKind]),
    CONSTRAINT [CK_InventoryMovements_CostPoolEpoch] CHECK ([CostPoolEpochId] IS NULL OR [CostPoolScope] IS NOT NULL),
    CONSTRAINT [UQ_InventoryMovements_Document_Line_Type] UNIQUE ([DocumentId], [DocumentType], [LineNumber], [MovementType]),
    CONSTRAINT [CK_InventoryMovements_Quantity] CHECK (
      ([QuantityChange] <> 0 AND ([ValuationMode] IS NULL OR [ValuationMode] <> N'ValueAdjustment'))
      OR ([QuantityChange] = 0 AND [ValuationMode] IS NOT NULL AND [ValuationMode] = N'ValueAdjustment')),
    CONSTRAINT [CK_InventoryMovements_CostPoolEvidence] CHECK (
      ([ValuationMode] IS NULL AND [CostPoolScope] IS NULL
        AND [PoolQuantityBefore] IS NULL AND [PoolQuantityAfter] IS NULL
        AND [PoolInventoryValueBefore] IS NULL AND [PoolInventoryValueAfter] IS NULL)
      OR ([ValuationMode] IS NOT NULL AND [CostPoolScope] IS NOT NULL
        AND [PoolQuantityBefore] IS NOT NULL AND [PoolQuantityAfter] IS NOT NULL
        AND [PoolInventoryValueBefore] IS NOT NULL AND [PoolInventoryValueAfter] IS NOT NULL
        AND [ValueChange] IS NOT NULL
        AND [ValuationMode] IN(N'AverageCost',N'WeightedAverageReceipt',N'SpecifiedCostIssue',N'ValueAdjustment')
        AND [CostPoolScope] IN(N'Business',N'SharedTenant',N'DamageWarehouse')
        AND [PoolQuantityAfter]=[PoolQuantityBefore]+[QuantityChange]
        AND [PoolInventoryValueAfter]-[PoolInventoryValueBefore]=[ValueChange]))
);

GO

CREATE INDEX [IX_InventoryMovements_Business_Warehouse]
    ON [dbo].[InventoryMovements] ([BusinessId], [WarehouseId], [OccurredAt]);

GO

CREATE INDEX [IX_InventoryMovements_Business_FilterOptions]
    ON [dbo].[InventoryMovements] ([BusinessId], [DocumentType], [MovementType])
    INCLUDE ([WarehouseId]);

