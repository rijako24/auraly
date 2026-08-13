CREATE TABLE [dbo].[ProductBrands]
(
    [ProductBrandId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductBrands_IsActive] DEFAULT 1,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductBrands] PRIMARY KEY CLUSTERED ([ProductBrandId]),
    CONSTRAINT [FK_ProductBrands_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_ProductBrands_Business_Name] UNIQUE ([BusinessId], [Name])
);
GO

-- Nombre funcional en UI: Unidad de venta. Conserva el nombre tecnico
-- ProductUnits ya definido en la arquitectura y reemplaza la escritura libre.
CREATE TABLE [dbo].[ProductUnits]
(
    [ProductUnitId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(24) NOT NULL,
    [Name] NVARCHAR(80) NOT NULL,
    [Symbol] NVARCHAR(16) NOT NULL,
    [AllowsFractionalQuantity] BIT NOT NULL,
    [DecimalPlaces] TINYINT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductUnits_IsActive] DEFAULT 1,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductUnits] PRIMARY KEY CLUSTERED ([ProductUnitId]),
    CONSTRAINT [FK_ProductUnits_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_ProductUnits_Business_Code] UNIQUE ([BusinessId], [Code]),
    CONSTRAINT [CK_ProductUnits_Decimals] CHECK ([DecimalPlaces] BETWEEN 0 AND 6),
    CONSTRAINT [CK_ProductUnits_Fraction] CHECK ([AllowsFractionalQuantity] = 1 OR [DecimalPlaces] = 0)
);
GO

CREATE TABLE [dbo].[ProductLinks]
(
    [ProductLinkId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ChildProductId] UNIQUEIDENTIFIER NOT NULL,
    [ParentProductId] UNIQUEIDENTIFIER NOT NULL,
    [InventoryFactor] DECIMAL(19,6) NULL,
    [PriceFactor] DECIMAL(19,6) NULL,
    [SharesInventory] BIT NOT NULL,
    [SharesPrice] BIT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductLinks_IsActive] DEFAULT 1,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductLinks] PRIMARY KEY CLUSTERED ([ProductLinkId]),
    CONSTRAINT [FK_ProductLinks_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_ProductLinks_Child] FOREIGN KEY ([ChildProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_ProductLinks_Parent] FOREIGN KEY ([ParentProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [UQ_ProductLinks_Business_Child] UNIQUE ([BusinessId], [ChildProductId]),
    CONSTRAINT [CK_ProductLinks_DifferentProducts] CHECK ([ChildProductId] <> [ParentProductId]),
    CONSTRAINT [CK_ProductLinks_InventoryFactor] CHECK (([SharesInventory] = 0 AND [InventoryFactor] IS NULL) OR ([SharesInventory] = 1 AND [InventoryFactor] > 0)),
    CONSTRAINT [CK_ProductLinks_PriceFactor] CHECK (([SharesPrice] = 0 AND [PriceFactor] IS NULL) OR ([SharesPrice] = 1 AND [PriceFactor] > 0))
);
GO

CREATE INDEX [IX_ProductLinks_Parent] ON [dbo].[ProductLinks] ([BusinessId], [ParentProductId], [IsActive]);
GO
