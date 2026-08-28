CREATE TABLE [dbo].[InventoryBalances]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [QuantityOnHand] DECIMAL(19,6) NOT NULL,
    [AverageUnitCost] DECIMAL(19,6) NOT NULL,
    [InventoryValue] DECIMAL(19,4) NOT NULL,
    [LastProcessingSequence] BIGINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryBalances]
        PRIMARY KEY CLUSTERED ([BusinessId], [WarehouseId], [ProductId]),
    CONSTRAINT [FK_InventoryBalances_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_InventoryBalances_Warehouses]
        FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]),
    CONSTRAINT [FK_InventoryBalances_Products]
        FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_InventoryBalances_AverageCost]
        CHECK ([AverageUnitCost] >= 0),
    CONSTRAINT [CK_InventoryBalances_Sequence]
        CHECK ([LastProcessingSequence] >= 0)
);

GO

CREATE INDEX [IX_InventoryBalances_WarehouseProduct]
    ON [dbo].[InventoryBalances] ([WarehouseId], [ProductId])
    INCLUDE ([QuantityOnHand], [AverageUnitCost], [InventoryValue]);

