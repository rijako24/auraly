CREATE TABLE [dbo].[PromotionConditions] (
    [PromotionConditionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [PromotionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ItemType] INT NOT NULL DEFAULT 0,
    [ProductId] UNIQUEIDENTIFIER NULL,
    [ServiceId] UNIQUEIDENTIFIER NULL,
    [ProductCategoryId] UNIQUEIDENTIFIER NULL,
    [ServiceCategoryId] UNIQUEIDENTIFIER NULL,
    [MinQuantity] DECIMAL(18, 2) NOT NULL DEFAULT 1,
    [MinSubtotal] DECIMAL(18, 2) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_PromotionConditions_Promotions] FOREIGN KEY ([PromotionId], [TenantId])
        REFERENCES [dbo].[Promotions] ([PromotionId], [TenantId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_PromotionConditions_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionConditions_Products] FOREIGN KEY ([ProductId])
        REFERENCES [dbo].[Products] ([ProductId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionConditions_Services] FOREIGN KEY ([ServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionConditions_ProductCategories] FOREIGN KEY ([ProductCategoryId])
        REFERENCES [dbo].[ProductCategories] ([ProductCategoryId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionConditions_ServiceCategories] FOREIGN KEY ([ServiceCategoryId])
        REFERENCES [dbo].[ServiceCategories] ([ServiceCategoryId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_PromotionConditions_ItemType] CHECK ([ItemType] IN (0, 1, 2, 3, 4, 5, 6)),
    CONSTRAINT [CK_PromotionConditions_MinQuantity] CHECK ([MinQuantity] > 0)
);

GO

CREATE INDEX [IX_PromotionConditions_TenantId] ON [dbo].[PromotionConditions] ([TenantId]);
GO
CREATE INDEX [IX_PromotionConditions_PromotionId] ON [dbo].[PromotionConditions] ([PromotionId]);
GO
CREATE INDEX [IX_PromotionConditions_ProductCategoryId] ON [dbo].[PromotionConditions] ([ProductCategoryId]) WHERE [ProductCategoryId] IS NOT NULL;
GO
CREATE INDEX [IX_PromotionConditions_ServiceCategoryId] ON [dbo].[PromotionConditions] ([ServiceCategoryId]) WHERE [ServiceCategoryId] IS NOT NULL;
GO
