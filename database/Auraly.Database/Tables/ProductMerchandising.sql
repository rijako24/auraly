CREATE TABLE [dbo].[ProductBrands]
(
    [ProductBrandId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductBrands_IsActive] DEFAULT 1,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductBrands] PRIMARY KEY CLUSTERED ([ProductBrandId]),
    CONSTRAINT [FK_ProductBrands_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [UQ_ProductBrands_Tenant_Brand] UNIQUE ([TenantId], [ProductBrandId]),
    CONSTRAINT [UQ_ProductBrands_Tenant_Name] UNIQUE ([TenantId], [Name])
);
GO

-- Nombre funcional en UI: Unidad de venta. Conserva el nombre tecnico
-- ProductUnits ya definido en la arquitectura y reemplaza la escritura libre.
CREATE TABLE [dbo].[ProductUnits]
(
    [ProductUnitId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(24) NOT NULL,
    [Name] NVARCHAR(80) NOT NULL,
    [Symbol] NVARCHAR(16) NOT NULL,
    [AllowsFractionalQuantity] BIT NOT NULL,
    [DecimalPlaces] TINYINT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductUnits_IsActive] DEFAULT 1,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductUnits] PRIMARY KEY CLUSTERED ([ProductUnitId]),
    CONSTRAINT [FK_ProductUnits_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [UQ_ProductUnits_Tenant_Code] UNIQUE ([TenantId], [Code]),
    CONSTRAINT [CK_ProductUnits_Decimals] CHECK ([DecimalPlaces] BETWEEN 0 AND 6),
    CONSTRAINT [CK_ProductUnits_Fraction] CHECK ([AllowsFractionalQuantity] = 1 OR [DecimalPlaces] = 0)
);
GO

CREATE TABLE [dbo].[ProductLinks]
(
    [ProductLinkId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ChildProductId] UNIQUEIDENTIFIER NOT NULL,
    [ParentProductId] UNIQUEIDENTIFIER NOT NULL,
    [InventoryFactor] DECIMAL(19,6) NULL,
    [PriceFactor] DECIMAL(19,6) NULL,
    [ConversionFactor] DECIMAL(19,6) NULL,
    [SharesInventory] BIT NOT NULL,
    [SharesPrice] BIT NOT NULL,
    [AllowsConversion] BIT NOT NULL CONSTRAINT [DF_ProductLinks_AllowsConversion] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductLinks_IsActive] DEFAULT 1,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_ProductLinks] PRIMARY KEY CLUSTERED ([ProductLinkId]),
    CONSTRAINT [FK_ProductLinks_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_ProductLinks_Child] FOREIGN KEY ([TenantId], [ChildProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [FK_ProductLinks_Parent] FOREIGN KEY ([TenantId], [ParentProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [UQ_ProductLinks_Tenant_Child] UNIQUE ([TenantId], [ChildProductId]),
    CONSTRAINT [CK_ProductLinks_DifferentProducts] CHECK ([ChildProductId] <> [ParentProductId]),
    CONSTRAINT [CK_ProductLinks_InventoryFactor] CHECK (([SharesInventory] = 0 AND [InventoryFactor] IS NULL) OR ([SharesInventory] = 1 AND [InventoryFactor] > 0)),
    CONSTRAINT [CK_ProductLinks_PriceFactor] CHECK (([SharesPrice] = 0 AND [PriceFactor] IS NULL) OR ([SharesPrice] = 1 AND [PriceFactor] > 0)),
    CONSTRAINT [CK_ProductLinks_ConversionFactor] CHECK (([AllowsConversion] = 0 AND [ConversionFactor] IS NULL) OR ([AllowsConversion] = 1 AND [SharesInventory] = 0 AND [ConversionFactor] > 0))
);
GO

CREATE INDEX [IX_ProductLinks_Parent] ON [dbo].[ProductLinks] ([TenantId], [ParentProductId], [IsActive]);
GO
