CREATE TABLE [dbo].[Products] (
    [ProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCode] NVARCHAR(64) NULL,
    [Reference] NVARCHAR(120) NULL,
    [BaseUnitCode] NVARCHAR(24) NULL,
    [TaxProfileId] UNIQUEIDENTIFIER NULL,
    [PurchaseTaxProfileId] UNIQUEIDENTIFIER NULL,
    [PurchaseTaxTreatment] NVARCHAR(32) NOT NULL CONSTRAINT [DF_Products_PurchaseTaxTreatment] DEFAULT N'DeductibleInputVat',
    [ProductCategoryId] UNIQUEIDENTIFIER NULL,
    [ProductBrandId] UNIQUEIDENTIFIER NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [ExternalProductId] NVARCHAR(300) NULL,
    [Source] INT NOT NULL DEFAULT 0,
    [Sku] NVARCHAR(100) NULL,
    [Name] NVARCHAR(250) NOT NULL,
    [Description] NVARCHAR(MAX) NULL,
    [CategoryName] NVARCHAR(150) NULL,
    [Currency] NVARCHAR(10) NOT NULL DEFAULT N'COP',
    [ManageStock] BIT NOT NULL DEFAULT 0,
    [IsGenericProduct] BIT NOT NULL CONSTRAINT [DF_Products_IsGenericProduct] DEFAULT 0,
    [UnitGrossWeightKg] DECIMAL(19, 6) NULL,
    [ConversionMaximumLossPercent] DECIMAL(9,6) NULL,
    [AllowsFractionalSale] BIT NOT NULL CONSTRAINT [DF_Products_AllowsFractionalSale] DEFAULT 0,
    [IsWeighable] BIT NOT NULL CONSTRAINT [DF_Products_IsWeighable] DEFAULT 0,
    [StockQuantity] DECIMAL(18, 2) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [RawPayloadJson] NVARCHAR(MAX) NULL,
    [SearchIndexVersion] INT NOT NULL DEFAULT 0,
    [LastSyncedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    [CreatedByUserId] UNIQUEIDENTIFIER NULL,
    [UpdatedByUserId] UNIQUEIDENTIFIER NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_Products_TaxProfiles] FOREIGN KEY ([TenantId], [TaxProfileId]) REFERENCES [dbo].[TaxProfiles] ([TenantId], [TaxProfileId]),
    CONSTRAINT [FK_Products_PurchaseTaxProfiles] FOREIGN KEY ([TenantId], [PurchaseTaxProfileId]) REFERENCES [dbo].[TaxProfiles] ([TenantId], [TaxProfileId]),
    CONSTRAINT [CK_Products_PurchaseTaxTreatment] CHECK ([PurchaseTaxTreatment] IN (N'DeductibleInputVat',N'CapitalizedCost',N'NotApplicable')),
    CONSTRAINT [FK_Products_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [UQ_Products_Tenant_Product] UNIQUE ([TenantId], [ProductId]),
    CONSTRAINT [FK_Products_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_ProductCategories] FOREIGN KEY ([TenantId], [ProductCategoryId])
        REFERENCES [dbo].[ProductCategories] ([TenantId], [ProductCategoryId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_ProductBrands] FOREIGN KEY ([TenantId], [ProductBrandId])
        REFERENCES [dbo].[ProductBrands] ([TenantId], [ProductBrandId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_Products_Source] CHECK ([Source] IN (0, 1)),
    CONSTRAINT [CK_Products_WeighableFractional] CHECK ([IsWeighable] = 0 OR [AllowsFractionalSale] = 1),
    CONSTRAINT [CK_Products_GenericNoStock] CHECK ([IsGenericProduct] = 0 OR [ManageStock] = 0),
    CONSTRAINT [CK_Products_UnitGrossWeightKg] CHECK ([UnitGrossWeightKg] IS NULL OR [UnitGrossWeightKg] > 0),
    CONSTRAINT [CK_Products_ConversionMaximumLossPercent] CHECK ([ConversionMaximumLossPercent] IS NULL OR [ConversionMaximumLossPercent] BETWEEN 0 AND 100),
    CONSTRAINT [CK_Products_CanonicalFields] CHECK (
        [ProductCode] IS NULL OR
        ([BaseUnitCode] IS NOT NULL AND [TaxProfileId] IS NOT NULL))
);

GO

CREATE UNIQUE INDEX [UX_Products_Tenant_ProductCode] ON [dbo].[Products] ([TenantId], [ProductCode]) WHERE [TenantId] IS NOT NULL AND [ProductCode] IS NOT NULL;
GO
CREATE INDEX [IX_Products_TenantId_Active_Name]
    ON [dbo].[Products] ([TenantId], [IsActive], [Name], [ProductId])
    INCLUDE ([ProductCode], [Sku], [Reference], [BaseUnitCode],
             [TaxProfileId], [PurchaseTaxProfileId], [PurchaseTaxTreatment],
             [UnitGrossWeightKg]);
GO
CREATE UNIQUE INDEX [IX_Products_TenantId_Connection_ExternalProductId]
    ON [dbo].[Products] ([TenantId], [IntegrationConnectionId], [ExternalProductId])
    WHERE [IntegrationConnectionId] IS NOT NULL AND [ExternalProductId] IS NOT NULL;
GO
