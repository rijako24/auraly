CREATE TABLE [dbo].[PromotionBenefits] (
    [PromotionBenefitId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [PromotionId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [BenefitType] INT NOT NULL,
    [TargetItemType] INT NOT NULL DEFAULT 0,
    [ProductId] UNIQUEIDENTIFIER NULL,
    [ServiceId] UNIQUEIDENTIFIER NULL,
    [CategoryName] NVARCHAR(150) NULL,
    [DiscountPercentage] DECIMAL(5, 2) NULL,
    [DiscountAmount] DECIMAL(18, 2) NULL,
    [FixedUnitPrice] DECIMAL(18, 2) NULL,
    [AppliesToQuantity] DECIMAL(18, 2) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_PromotionBenefits_Promotions] FOREIGN KEY ([PromotionId])
        REFERENCES [dbo].[Promotions] ([PromotionId])
        ON DELETE CASCADE,
    CONSTRAINT [FK_PromotionBenefits_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionBenefits_Products] FOREIGN KEY ([ProductId])
        REFERENCES [dbo].[Products] ([ProductId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_PromotionBenefits_Services] FOREIGN KEY ([ServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION,
    CONSTRAINT [CK_PromotionBenefits_BenefitType] CHECK ([BenefitType] IN (0, 1, 2, 3)),
    CONSTRAINT [CK_PromotionBenefits_TargetItemType] CHECK ([TargetItemType] IN (0, 1, 2, 3, 4, 5, 6))
);

GO

CREATE INDEX [IX_PromotionBenefits_BusinessId] ON [dbo].[PromotionBenefits] ([BusinessId]);
GO
CREATE INDEX [IX_PromotionBenefits_PromotionId] ON [dbo].[PromotionBenefits] ([PromotionId]);
GO
