CREATE TABLE [dbo].[TaxProfiles] (
    [TaxProfileId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [DianTaxCode] NVARCHAR(8) NOT NULL CONSTRAINT [DF_TaxProfiles_DianTaxCode] DEFAULT N'01',
    [Name] NVARCHAR(120) NOT NULL,
    [Rate] DECIMAL(9,6) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_TaxProfiles_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [UQ_TaxProfiles_Tenant_Code] UNIQUE ([TenantId], [Code]),
    CONSTRAINT [UQ_TaxProfiles_Tenant_Profile] UNIQUE ([TenantId], [TaxProfileId]),
    CONSTRAINT [CK_TaxProfiles_Rate] CHECK ([Rate] BETWEEN 0 AND 100)
);
GO

CREATE TABLE [dbo].[ProductBarcodes] (
    [ProductBarcodeId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Barcode] NVARCHAR(64) NOT NULL,
    [IsPrimary] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductBarcodes_Products] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [UQ_ProductBarcodes_Tenant_Barcode] UNIQUE ([TenantId], [Barcode])
);
GO
CREATE INDEX [IX_ProductBarcodes_Product] ON [dbo].[ProductBarcodes] ([ProductId], [IsActive]);
GO

CREATE TABLE [dbo].[ProductIdentifiers] (
    [ProductIdentifierId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [IdentifierType] NVARCHAR(32) NOT NULL,
    [Value] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_ProductIdentifiers_Products] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [UQ_ProductIdentifiers_Tenant_Type_Value] UNIQUE ([TenantId], [IdentifierType], [Value])
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
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [Strategy] NVARCHAR(48) NOT NULL CONSTRAINT [DF_PriceChannels_Strategy] DEFAULT N'TieredProductPrice',
    [Value] DECIMAL(19,6) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_PriceChannels_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [CK_PriceChannels_Strategy] CHECK ([Strategy] IN (N'TieredProductPrice',N'PercentageOverBasePrice',N'MarginOverLatestCost',N'FixedMarginOverAverageCost',N'SellAtAverageCost',N'ProductMarginAdjustment')),
    CONSTRAINT [CK_PriceChannels_Value] CHECK (([Strategy] IN (N'TieredProductPrice',N'SellAtAverageCost') AND [Value] IS NULL) OR ([Strategy]=N'PercentageOverBasePrice' AND [Value] BETWEEN -100 AND 1000) OR ([Strategy]=N'MarginOverLatestCost' AND [Value] BETWEEN 0 AND 99.999999) OR ([Strategy]=N'FixedMarginOverAverageCost' AND [Value] BETWEEN 0 AND 99.999999) OR ([Strategy]=N'ProductMarginAdjustment' AND [Value] BETWEEN -99.999999 AND 99.999999)),
    CONSTRAINT [UQ_PriceChannels_Tenant_Code] UNIQUE ([TenantId], [Code])
);
GO

CREATE TABLE [dbo].[ProductPrices] (
    [ProductPriceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [PreparedAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_ProductPrices_PreparedAmount] DEFAULT (0),
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
    CONSTRAINT [CK_ProductPrices_PreparedAmount] CHECK ([PreparedAmount] >= 0),
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
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [Identification] NVARCHAR(40) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [PurchaseEvidencePolicy] NVARCHAR(40) NULL,
    [DefaultPaymentDueDays] INT NOT NULL CONSTRAINT [DF_Suppliers_DefaultPaymentDueDays] DEFAULT (30),
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_Suppliers_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_Suppliers_Parties] FOREIGN KEY ([TenantId], [PartyId]) REFERENCES [dbo].[Parties] ([TenantId], [PartyId]),
    CONSTRAINT [UQ_Suppliers_Tenant_Identification] UNIQUE ([TenantId], [Identification]),
    CONSTRAINT [UQ_Suppliers_Tenant_Supplier] UNIQUE ([TenantId], [SupplierId]),
    CONSTRAINT [CK_Suppliers_DefaultPaymentDueDays] CHECK ([DefaultPaymentDueDays] BETWEEN 0 AND 3650),
    CONSTRAINT [CK_Suppliers_PurchaseEvidencePolicy] CHECK ([PurchaseEvidencePolicy] IS NULL OR [PurchaseEvidencePolicy] IN
      (N'SupplierElectronicInvoice',N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher'))
);
GO
CREATE UNIQUE INDEX [UX_Suppliers_Tenant_Party]
    ON [dbo].[Suppliers] ([TenantId], [PartyId]) WHERE [PartyId] IS NOT NULL;
GO

CREATE TABLE [dbo].[SupplierProducts] (
    [SupplierProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [SupplierProductCode] NVARCHAR(120) NULL,
    [PurchasePresentationName] NVARCHAR(80) NOT NULL CONSTRAINT [DF_SupplierProducts_PurchasePresentationName] DEFAULT N'Unidad',
    [UnitsPerPresentation] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_SupplierProducts_UnitsPerPresentation] DEFAULT 1,
    [IsPrimary] BIT NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_SupplierProducts_Products] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [FK_SupplierProducts_Suppliers] FOREIGN KEY ([TenantId], [SupplierId]) REFERENCES [dbo].[Suppliers] ([TenantId], [SupplierId]),
    CONSTRAINT [UQ_SupplierProducts_Tenant_Product_Supplier] UNIQUE ([TenantId], [ProductId], [SupplierId]),
    CONSTRAINT [UQ_SupplierProducts_Product_Supplier] UNIQUE ([ProductId], [SupplierId]),
    CONSTRAINT [CK_SupplierProducts_UnitsPerPresentation] CHECK ([UnitsPerPresentation] > 0)
);
GO
CREATE UNIQUE INDEX [UX_SupplierProducts_Primary] ON [dbo].[SupplierProducts] ([TenantId], [ProductId])
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
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [HighWaterMark] BIGINT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ExpiresAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [FK_CatalogSyncSessions_EnrolledDevices] FOREIGN KEY ([DeviceId]) REFERENCES [dbo].[EnrolledDevices] ([DeviceId]),
    CONSTRAINT [FK_CatalogSyncSessions_Warehouses] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId])
);
GO
