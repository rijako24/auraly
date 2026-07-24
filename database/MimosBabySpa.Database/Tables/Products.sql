CREATE TABLE [dbo].[Products] (
    [ProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
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
    [StockQuantity] DECIMAL(18, 2) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [RawPayloadJson] NVARCHAR(MAX) NULL,
    [SearchIndexVersion] INT NOT NULL DEFAULT 0,
    [LastSyncedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Products_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_IntegrationConnections] FOREIGN KEY ([IntegrationConnectionId])
        REFERENCES [dbo].[IntegrationConnections] ([IntegrationConnectionId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_ProductCategories] FOREIGN KEY ([ProductCategoryId])
        REFERENCES [dbo].[ProductCategories] ([ProductCategoryId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_Products_Source] CHECK ([Source] IN (0, 1))
);

GO

CREATE INDEX [IX_Products_BusinessId] ON [dbo].[Products] ([BusinessId]);
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
