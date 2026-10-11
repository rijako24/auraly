CREATE TABLE [sales].[RestaurantAccountAdditions]
(
    [AdditionId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [AccountId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCode] NVARCHAR(64) NOT NULL,
    [Description] NVARCHAR(250) NOT NULL,
    [UnitCode] NVARCHAR(24) NOT NULL,
    [TaxCode] NVARCHAR(16) NOT NULL,
    [TaxRate] DECIMAL(9,4) NOT NULL,
    [Quantity] DECIMAL(18,4) NOT NULL,
    [OriginalQuantity] DECIMAL(18,4) NOT NULL,
    [AllowsFractionalSale] BIT NOT NULL,
    [PublicUnitPrice] DECIMAL(19,4) NOT NULL,
    [PromotionDiscountPerUnit] DECIMAL(19,4) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [PriceSource] NVARCHAR(24) NOT NULL,
    [AddedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AddedByName] NVARCHAR(201) NOT NULL,
    [AddedAt] DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL CONSTRAINT [DF_RestaurantAccountAdditions_Status] DEFAULT N'Pending',
    [CancelledAt] DATETIMEOFFSET(7) NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    [PreparationNote] NVARCHAR(500) NULL,
    [ProductRecipeVersionId] UNIQUEIDENTIFIER NULL,
    [ManageStockAtSend] BIT NULL,
    [ReplacementOfCancellationId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_RestaurantAccountAdditions] PRIMARY KEY ([AdditionId]),
    CONSTRAINT [UQ_RestaurantAccountAdditions_Account_Id] UNIQUE ([AccountId],[AdditionId]),
    CONSTRAINT [FK_RestaurantAccountAdditions_Account] FOREIGN KEY ([BusinessId],[AccountId])
        REFERENCES [sales].[RestaurantAccounts] ([BusinessId],[AccountId]),
    CONSTRAINT [FK_RestaurantAccountAdditions_Product] FOREIGN KEY ([TenantId],[ProductId])
        REFERENCES [dbo].[Products] ([TenantId],[ProductId]),
    CONSTRAINT [FK_RestaurantAccountAdditions_Replacement] FOREIGN KEY ([ReplacementOfCancellationId])
        REFERENCES [sales].[RestaurantCancellationRequests] ([CancellationId]),
    CONSTRAINT [FK_RestaurantAccountAdditions_RecipeVersion] FOREIGN KEY ([TenantId],[ProductRecipeVersionId])
        REFERENCES [dbo].[ProductRecipeVersions] ([TenantId],[ProductRecipeVersionId]),
    CONSTRAINT [FK_RestaurantAccountAdditions_User] FOREIGN KEY ([AddedByUserId],[TenantId])
        REFERENCES [dbo].[AppUsers] ([UserId],[TenantId]),
    CONSTRAINT [FK_RestaurantAccountAdditions_Business] FOREIGN KEY ([BusinessId],[TenantId])
        REFERENCES [dbo].[Businesses] ([BusinessId],[TenantId]),
    CONSTRAINT [CK_RestaurantAccountAdditions_Quantity] CHECK
        ([Quantity]>0 AND [OriginalQuantity]>0),
    CONSTRAINT [CK_RestaurantAccountAdditions_Price] CHECK
        ([TaxRate]>=0 AND [PublicUnitPrice]>=0 AND [PromotionDiscountPerUnit]>=0
         AND [PromotionDiscountPerUnit]<=[PublicUnitPrice]),
    CONSTRAINT [CK_RestaurantAccountAdditions_Status] CHECK
        (([Status] IN (N'Pending',N'Sent',N'Direct') AND [CancelledAt] IS NULL)
         OR ([Status]=N'Cancelled' AND [CancelledAt] IS NOT NULL))
);
GO
CREATE INDEX [IX_RestaurantAccountAdditions_Account_Added]
    ON [sales].[RestaurantAccountAdditions] ([AccountId],[AddedAt],[AdditionId]);
GO
CREATE UNIQUE INDEX [UX_RestaurantAccountAdditions_Replacement]
    ON [sales].[RestaurantAccountAdditions] ([ReplacementOfCancellationId])
    WHERE [ReplacementOfCancellationId] IS NOT NULL;
GO
