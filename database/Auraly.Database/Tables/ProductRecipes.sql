CREATE TABLE [catalog].[ProductRecipeVersions] (
    [ProductRecipeVersionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Version] INT NOT NULL,
    [IsEnabled] BIT NOT NULL,
    [PublishedAt] DATETIMEOFFSET(7) NOT NULL,
    [PublishedByUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_ProductRecipeVersions] PRIMARY KEY ([ProductRecipeVersionId]),
    CONSTRAINT [UQ_ProductRecipeVersions_ProductVersion] UNIQUE ([TenantId], [ProductId], [Version]),
    CONSTRAINT [UQ_ProductRecipeVersions_TenantVersionId] UNIQUE ([TenantId], [ProductRecipeVersionId]),
    CONSTRAINT [FK_ProductRecipeVersions_Product] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [CK_ProductRecipeVersions_Version] CHECK ([Version] > 0)
);
GO

CREATE TABLE [catalog].[ProductRecipeIngredients] (
    [ProductRecipeVersionId] UNIQUEIDENTIFIER NOT NULL,
    [LineNumber] INT NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [IngredientProductId] UNIQUEIDENTIFIER NOT NULL,
    [QuantityPerUnit] DECIMAL(19,6) NOT NULL,
    [BaseUnitCodeSnapshot] NVARCHAR(24) NOT NULL,
    CONSTRAINT [PK_ProductRecipeIngredients] PRIMARY KEY ([ProductRecipeVersionId], [LineNumber]),
    CONSTRAINT [UQ_ProductRecipeIngredients_VersionProduct] UNIQUE ([ProductRecipeVersionId], [IngredientProductId]),
    CONSTRAINT [FK_ProductRecipeIngredients_Version] FOREIGN KEY ([TenantId], [ProductRecipeVersionId]) REFERENCES [catalog].[ProductRecipeVersions] ([TenantId], [ProductRecipeVersionId]),
    CONSTRAINT [FK_ProductRecipeIngredients_Product] FOREIGN KEY ([TenantId], [IngredientProductId]) REFERENCES [dbo].[Products] ([TenantId], [ProductId]),
    CONSTRAINT [CK_ProductRecipeIngredients_Quantity] CHECK ([QuantityPerUnit] > 0)
);
GO

CREATE INDEX [IX_ProductRecipeIngredients_Ingredient] ON [catalog].[ProductRecipeIngredients] ([TenantId], [IngredientProductId]);
GO
