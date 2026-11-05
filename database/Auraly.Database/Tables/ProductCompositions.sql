CREATE TABLE [catalog].[ProductCompositionVersions] (
    [ProductRecipeVersionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Version] INT NOT NULL,
    [IsEnabled] BIT NOT NULL,
    [CompositionKind] NVARCHAR(16) NOT NULL CONSTRAINT [DF_ProductCompositionVersions_CompositionKind] DEFAULT N'Ingredients',
    CONSTRAINT [CK_ProductCompositionVersions_CompositionKind] CHECK ([CompositionKind] IN (N'Ingredients',N'Combo')),
    [PublishedAt] DATETIMEOFFSET(7) NOT NULL,
    [PublishedByUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_ProductCompositionVersions] PRIMARY KEY ([ProductRecipeVersionId]),
    CONSTRAINT [UQ_ProductCompositionVersions_ProductVersion] UNIQUE ([TenantId], [ProductId], [Version]),
    CONSTRAINT [UQ_ProductCompositionVersions_TenantVersionId] UNIQUE ([TenantId], [ProductRecipeVersionId]),
    CONSTRAINT [FK_ProductCompositionVersions_Product] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [CK_ProductCompositionVersions_Version] CHECK ([Version] > 0)
);
GO

CREATE TABLE [catalog].[ProductCompositionComponents] (
    [ProductRecipeVersionId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [IngredientProductId] UNIQUEIDENTIFIER NOT NULL,
    [QuantityPerUnit] DECIMAL(19,6) NOT NULL,
    [BaseUnitCodeSnapshot] NVARCHAR(24) NOT NULL,
    [ProductCodeSnapshot] NVARCHAR(80) NULL,
    [ProductNameSnapshot] NVARCHAR(300) NULL,
    CONSTRAINT [PK_ProductCompositionComponents] PRIMARY KEY ([ProductRecipeVersionId], [LineNumber]),
    CONSTRAINT [UQ_ProductCompositionComponents_VersionProduct] UNIQUE ([ProductRecipeVersionId], [IngredientProductId]),
    CONSTRAINT [FK_ProductCompositionComponents_Version] FOREIGN KEY ([TenantId], [ProductRecipeVersionId]) REFERENCES [catalog].[ProductCompositionVersions] ([TenantId], [ProductRecipeVersionId]),
    CONSTRAINT [FK_ProductCompositionComponents_Product] FOREIGN KEY ([TenantId], [IngredientProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [CK_ProductCompositionComponents_Quantity] CHECK ([QuantityPerUnit] > 0)
);
GO

CREATE INDEX [IX_ProductCompositionComponents_Ingredient] ON [catalog].[ProductCompositionComponents] ([TenantId], [IngredientProductId]);
GO
