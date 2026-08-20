CREATE TABLE [dbo].[Warehouses]
(
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [AllowNegativeStockSales] BIT NOT NULL,
    [IsSystem] BIT NOT NULL CONSTRAINT [DF_Warehouses_IsSystem] DEFAULT (0),
    [UseForSales] BIT NOT NULL CONSTRAINT [DF_Warehouses_UseForSales] DEFAULT (1),
    [UseForGoodsReceipts] BIT NOT NULL CONSTRAINT [DF_Warehouses_UseForGoodsReceipts] DEFAULT (1),
    [IsInventoryVisible] BIT NOT NULL CONSTRAINT [DF_Warehouses_IsInventoryVisible] DEFAULT (1),
    [PriceFormationCostBasis] NVARCHAR(32) NOT NULL CONSTRAINT [DF_Warehouses_PriceFormationCostBasis] DEFAULT N'LatestReceiptCost',
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Warehouses_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_Warehouses] PRIMARY KEY CLUSTERED ([WarehouseId]),
    CONSTRAINT [FK_Warehouses_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_Warehouses_Business_Code] UNIQUE ([BusinessId], [Code]),
    CONSTRAINT [UQ_Warehouses_Business_Warehouse] UNIQUE ([BusinessId], [WarehouseId]),
    CONSTRAINT [CK_Warehouses_PriceFormationCostBasis] CHECK ([PriceFormationCostBasis] IN (N'LatestReceiptCost',N'WeightedAverageCost'))
);

GO
