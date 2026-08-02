CREATE TABLE [dbo].[TaxProfiles] (
    [TaxProfileId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [Rate] DECIMAL(9,6) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_TaxProfiles_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_TaxProfiles_Business_Code] UNIQUE ([BusinessId], [Code]),
    CONSTRAINT [CK_TaxProfiles_Rate] CHECK ([Rate] BETWEEN 0 AND 100)
);
GO

CREATE TABLE [dbo].[ProductBarcodes] (
    [ProductBarcodeId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Barcode] NVARCHAR(64) NOT NULL,
    [IsPrimary] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductBarcodes_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [UQ_ProductBarcodes_Business_Barcode] UNIQUE ([BusinessId], [Barcode])
);
GO
CREATE INDEX [IX_ProductBarcodes_Product] ON [dbo].[ProductBarcodes] ([ProductId], [IsActive]);
GO

CREATE TABLE [dbo].[ProductIdentifiers] (
    [ProductIdentifierId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [IdentifierType] NVARCHAR(32) NOT NULL,
    [Value] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_ProductIdentifiers_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [UQ_ProductIdentifiers_Business_Type_Value] UNIQUE ([BusinessId], [IdentifierType], [Value])
);
GO

CREATE TABLE [dbo].[ProductScaleConfigurations] (
    [ProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [ScaleCode] NVARCHAR(16) NOT NULL,
    [BarcodePrefix] NVARCHAR(8) NOT NULL,
    [EmbeddedValueType] NVARCHAR(16) NOT NULL,
    [ValueStart] INT NOT NULL,
    [ValueLength] INT NOT NULL,
    [DecimalPlaces] INT NOT NULL,
    [IsActive] BIT NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductScaleConfigurations_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_ProductScaleConfigurations_Type] CHECK ([EmbeddedValueType] IN (N'Weight', N'Price')),
    CONSTRAINT [CK_ProductScaleConfigurations_Range] CHECK ([ValueStart] >= 0 AND [ValueLength] > 0 AND [DecimalPlaces] BETWEEN 0 AND 6)
);
GO

CREATE TABLE [dbo].[PriceChannels] (
    [PriceChannelId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_PriceChannels_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_PriceChannels_Business_Code] UNIQUE ([BusinessId], [Code])
);
GO

CREATE TABLE [dbo].[ProductPrices] (
    [ProductPriceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [CostBasisType] NVARCHAR(32) NULL,
    [CostBasisAmount] DECIMAL(19,6) NULL,
    [TargetMarginPercent] DECIMAL(9,6) NULL,
    [EffectiveMarginPercent] DECIMAL(9,6) NULL,
    [InputMode] NVARCHAR(16) NULL,
    [RoundingIncrement] DECIMAL(19,4) NULL,
    [RoundingMode] NVARCHAR(16) NULL,
    [PublishedByUserId] UNIQUEIDENTIFIER NULL,
    [PublishedAt] DATETIMEOFFSET(7) NULL,
    [ValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [ValidUntil] DATETIMEOFFSET(7) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductPrices_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_ProductPrices_Amount] CHECK ([Amount] >= 0),
    CONSTRAINT [CK_ProductPrices_CostBasis] CHECK ([CostBasisAmount] IS NULL OR [CostBasisAmount] >= 0),
    CONSTRAINT [CK_ProductPrices_Margin] CHECK ([TargetMarginPercent] IS NULL OR [TargetMarginPercent] BETWEEN 0 AND 99.999999),
    CONSTRAINT [CK_ProductPrices_InputMode] CHECK ([InputMode] IS NULL OR [InputMode] IN (N'Margin',N'SalePrice')),
    CONSTRAINT [CK_ProductPrices_Rounding] CHECK (([RoundingIncrement] IS NULL AND [RoundingMode] IS NULL) OR ([RoundingIncrement] > 0 AND [RoundingMode] IN (N'Nearest',N'Up',N'Down'))),
    CONSTRAINT [CK_ProductPrices_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [ValidFrom])
);
GO
CREATE UNIQUE INDEX [UX_ProductPrices_Active] ON [dbo].[ProductPrices] ([BusinessId], [ProductId])
    WHERE [IsActive] = 1;
GO

CREATE TABLE [dbo].[Suppliers] (
    [SupplierId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Identification] NVARCHAR(40) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_Suppliers_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_Suppliers_Business_Identification] UNIQUE ([BusinessId], [Identification])
);
GO

CREATE TABLE [dbo].[SupplierProducts] (
    [SupplierProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierProductCode] NVARCHAR(120) NULL,
    [IsPrimary] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_SupplierProducts_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_SupplierProducts_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([SupplierId]),
    CONSTRAINT [UQ_SupplierProducts_Business_Product_Supplier] UNIQUE ([BusinessId], [ProductId], [SupplierId])
);
GO
CREATE UNIQUE INDEX [UX_SupplierProducts_Primary] ON [dbo].[SupplierProducts] ([BusinessId], [ProductId])
    WHERE [IsPrimary] = 1 AND [IsActive] = 1;
GO

CREATE TABLE [dbo].[SupplierCostAgreements] (
    [SupplierCostAgreementId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [SupplierProductId] UNIQUEIDENTIFIER NOT NULL,
    [BaseUnitCost] DECIMAL(19,4) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [ValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [ValidUntil] DATETIMEOFFSET(7) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_SupplierCostAgreements_SupplierProducts] FOREIGN KEY ([SupplierProductId]) REFERENCES [dbo].[SupplierProducts] ([SupplierProductId]),
    CONSTRAINT [CK_SupplierCostAgreements_Cost] CHECK ([BaseUnitCost] >= 0),
    CONSTRAINT [CK_SupplierCostAgreements_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [ValidFrom])
);
GO
CREATE UNIQUE INDEX [UX_SupplierCostAgreements_Active] ON [dbo].[SupplierCostAgreements] ([SupplierProductId])
    WHERE [IsActive] = 1;
GO

CREATE TABLE [dbo].[CatalogChanges] (
    [CatalogChangeId] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ChangeKind] NVARCHAR(32) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_CatalogChanges_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_CatalogChanges_Kind] CHECK ([ChangeKind] IN (N'Upsert', N'Tombstone'))
);
GO
CREATE INDEX [IX_CatalogChanges_Scope_Cursor] ON [dbo].[CatalogChanges]
    ([BusinessId], [CatalogChangeId]);
GO

CREATE TABLE [dbo].[CatalogSyncSessions] (
    [CatalogSyncSessionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [DeviceId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [HighWaterMark] BIGINT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_CatalogSyncSessions_EnrolledDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId])
);
GO
