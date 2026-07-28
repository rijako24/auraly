CREATE TABLE [dbo].[Products] (
    [ProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCode] NVARCHAR(64) NULL,
    [Reference] NVARCHAR(120) NULL,
    [BaseUnitCode] NVARCHAR(24) NULL,
    [TaxProfileId] UNIQUEIDENTIFIER NULL,
    [ProductCategoryId] UNIQUEIDENTIFIER NULL,
    [IntegrationConnectionId] UNIQUEIDENTIFIER NULL,
    [ExternalProductId] NVARCHAR(300) NULL,
    [Source] INT NOT NULL DEFAULT 0,
    [Sku] NVARCHAR(100) NULL,
    [Name] NVARCHAR(250) NOT NULL,
    [Description] NVARCHAR(MAX) NULL,
    [CategoryName] NVARCHAR(150) NULL,
    [UnitPrice] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [Currency] NVARCHAR(10) NOT NULL DEFAULT N'COP',
    [ManageStock] BIT NOT NULL DEFAULT 0,
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
    CONSTRAINT [FK_Products_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([TenantId]),
    CONSTRAINT [FK_Products_TaxProfiles] FOREIGN KEY ([TaxProfileId]) REFERENCES [dbo].[TaxProfiles] ([TaxProfileId]),
    CONSTRAINT [FK_Products_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_ProductCategories] FOREIGN KEY ([ProductCategoryId])
        REFERENCES [dbo].[ProductCategories] ([ProductCategoryId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_Products_Source] CHECK ([Source] IN (0, 1)),
    CONSTRAINT [CK_Products_CanonicalFields] CHECK (
        [ProductCode] IS NULL OR
        ([TenantId] IS NOT NULL AND [BaseUnitCode] IS NOT NULL AND [TaxProfileId] IS NOT NULL))
);

GO

CREATE INDEX [IX_Products_BusinessId] ON [dbo].[Products] ([BusinessId]);
GO
CREATE UNIQUE INDEX [UX_Products_Business_ProductCode] ON [dbo].[Products] ([BusinessId], [ProductCode]) WHERE [ProductCode] IS NOT NULL;
GO
CREATE INDEX [IX_Products_BusinessId_Name] ON [dbo].[Products] ([BusinessId], [Name]);
GO
CREATE INDEX [IX_Products_BusinessId_CategoryName] ON [dbo].[Products] ([BusinessId], [CategoryName]);
GO
CREATE INDEX [IX_Products_BusinessId_Sku] ON [dbo].[Products] ([BusinessId], [Sku]);
GO
CREATE UNIQUE INDEX [IX_Products_BusinessId_Connection_ExternalProductId]
    ON [dbo].[Products] ([BusinessId], [IntegrationConnectionId], [ExternalProductId])
    WHERE [IntegrationConnectionId] IS NOT NULL AND [ExternalProductId] IS NOT NULL;
GO
