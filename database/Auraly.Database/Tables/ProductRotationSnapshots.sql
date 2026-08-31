CREATE TABLE [reporting].[ProductRotationSnapshots]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [WindowEndDate] DATE NOT NULL,
    [GrossUnitsSold30Days] DECIMAL(19,6) NOT NULL,
    [ReturnedUnits30Days] DECIMAL(19,6) NOT NULL,
    [NetUnitsSold30Days] DECIMAL(19,6) NOT NULL,
    [GrossUnitsSold90Days] DECIMAL(19,6) NOT NULL,
    [ReturnedUnits90Days] DECIMAL(19,6) NOT NULL,
    [NetUnitsSold90Days] DECIMAL(19,6) NOT NULL,
    [DailyDemand90Days] DECIMAL(19,6) NOT NULL,
    [ProjectionVersion] SMALLINT NOT NULL,
    [CalculatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_ProductRotationSnapshots] PRIMARY KEY([BusinessId],[WarehouseId],[ProductId]),
    CONSTRAINT [FK_ProductRotationSnapshots_Business] FOREIGN KEY([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_ProductRotationSnapshots_Warehouse] FOREIGN KEY([WarehouseId]) REFERENCES [dbo].[Warehouses]([WarehouseId]),
    CONSTRAINT [FK_ProductRotationSnapshots_Product] FOREIGN KEY([ProductId]) REFERENCES [dbo].[Products]([ProductId]),
    CONSTRAINT [CK_ProductRotationSnapshots_Values] CHECK([GrossUnitsSold30Days]>=0 AND [ReturnedUnits30Days]>=0 AND [GrossUnitsSold90Days]>=0 AND [ReturnedUnits90Days]>=0 AND [DailyDemand90Days]>=0 AND [ProjectionVersion]>0)
);
GO
CREATE INDEX [IX_ProductRotationSnapshots_Product] ON [reporting].[ProductRotationSnapshots]([BusinessId],[ProductId]) INCLUDE([WarehouseId],[NetUnitsSold30Days],[NetUnitsSold90Days],[DailyDemand90Days],[CalculatedAt]);
GO
