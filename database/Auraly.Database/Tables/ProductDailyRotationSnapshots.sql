CREATE TABLE [reporting].[ProductDailyRotationSnapshots]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessLocalDate] DATE NOT NULL,
    [NetQuantity] DECIMAL(19,6) NOT NULL,
    [ProjectionVersion] SMALLINT NOT NULL,
    [CalculatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ProductDailyRotationSnapshots] PRIMARY KEY CLUSTERED
        ([BusinessId],[WarehouseId],[ProductId],[BusinessLocalDate]),
    CONSTRAINT [FK_ProductDailyRotationSnapshots_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_ProductDailyRotationSnapshots_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses]([WarehouseId]),
    CONSTRAINT [FK_ProductDailyRotationSnapshots_Product] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products]([ProductId]),
    CONSTRAINT [CK_ProductDailyRotationSnapshots_Version] CHECK ([ProjectionVersion]>0)
);
GO
CREATE INDEX [IX_ProductDailyRotationSnapshots_Window]
    ON [reporting].[ProductDailyRotationSnapshots]
       ([BusinessId],[WarehouseId],[BusinessLocalDate],[ProductId])
    INCLUDE ([NetQuantity]);
GO
