CREATE TABLE [dbo].[Warehouses]
(
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [LocationId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [AllowNegativeStockSales] BIT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Warehouses_IsActive] DEFAULT (1),
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_Warehouses] PRIMARY KEY CLUSTERED ([WarehouseId]),
    CONSTRAINT [FK_Warehouses_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_Warehouses_BusinessLocations] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[BusinessLocations] ([LocationId]),
    CONSTRAINT [UQ_Warehouses_Business_Code] UNIQUE ([BusinessId], [Code])
);

GO

CREATE INDEX [IX_Warehouses_Business_Location]
    ON [dbo].[Warehouses] ([BusinessId], [LocationId]);

