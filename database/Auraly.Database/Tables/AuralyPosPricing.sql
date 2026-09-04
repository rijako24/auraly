CREATE TABLE [dbo].[PriceChannelItems] (
    [PriceChannelItemId] UNIQUEIDENTIFIER NOT NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [MinimumQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_PriceChannelItems_MinimumQuantity] DEFAULT (1),
    [Amount] DECIMAL(19,4) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [ValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [ValidUntil] DATETIMEOFFSET(7) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PriceChannelItems] PRIMARY KEY ([PriceChannelItemId]),
    CONSTRAINT [FK_PriceChannelItems_PriceChannels] FOREIGN KEY ([PriceChannelId]) REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [FK_PriceChannelItems_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_PriceChannelItems_Amount] CHECK ([Amount] >= 0),
    CONSTRAINT [CK_PriceChannelItems_MinimumQuantity] CHECK ([MinimumQuantity] > 0),
    CONSTRAINT [CK_PriceChannelItems_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [ValidFrom])
);
GO
CREATE UNIQUE INDEX [UX_PriceChannelItems_Active]
    ON [dbo].[PriceChannelItems] ([PriceChannelId], [ProductId], [MinimumQuantity])
    WHERE [IsActive] = 1;
GO

CREATE TABLE [dbo].[PriceChannelExclusions] (
    [PriceChannelExclusionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PriceChannelExclusions_Id] DEFAULT NEWID(),
    [PriceChannelId] UNIQUEIDENTIFIER NOT NULL,
    [ScopeType] NVARCHAR(16) NOT NULL CONSTRAINT [DF_PriceChannelExclusions_ScopeType] DEFAULT N'Product',
    [ProductId] UNIQUEIDENTIFIER NULL,
    [ProductCategoryId] UNIQUEIDENTIFIER NULL,
    [ProductBrandId] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PriceChannelExclusions] PRIMARY KEY ([PriceChannelExclusionId]),
    CONSTRAINT [FK_PriceChannelExclusions_PriceChannels] FOREIGN KEY ([PriceChannelId]) REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [FK_PriceChannelExclusions_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [FK_PriceChannelExclusions_ProductCategories] FOREIGN KEY ([ProductCategoryId]) REFERENCES [dbo].[ProductCategories] ([ProductCategoryId]),
    CONSTRAINT [FK_PriceChannelExclusions_ProductBrands] FOREIGN KEY ([ProductBrandId]) REFERENCES [dbo].[ProductBrands] ([ProductBrandId]),
    CONSTRAINT [CK_PriceChannelExclusions_Scope] CHECK (
        ([ScopeType] = N'Product' AND [ProductId] IS NOT NULL AND [ProductCategoryId] IS NULL AND [ProductBrandId] IS NULL) OR
        ([ScopeType] = N'Category' AND [ProductId] IS NULL AND [ProductCategoryId] IS NOT NULL AND [ProductBrandId] IS NULL) OR
        ([ScopeType] = N'Brand' AND [ProductId] IS NULL AND [ProductCategoryId] IS NULL AND [ProductBrandId] IS NOT NULL))
);
GO
CREATE UNIQUE INDEX [UX_PriceChannelExclusions_Product]
    ON [dbo].[PriceChannelExclusions] ([PriceChannelId], [ProductId])
    WHERE [ProductId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_PriceChannelExclusions_Category]
    ON [dbo].[PriceChannelExclusions] ([PriceChannelId], [ProductCategoryId])
    WHERE [ProductCategoryId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_PriceChannelExclusions_Brand]
    ON [dbo].[PriceChannelExclusions] ([PriceChannelId], [ProductBrandId])
    WHERE [ProductBrandId] IS NOT NULL;
GO

CREATE TABLE [dbo].[CommerceSellers] (
    [SellerId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [DefaultCommissionPercent] DECIMAL(9,6) NULL,
    [CommissionBasis] NVARCHAR(24) NOT NULL,
    [CommissionTrigger] NVARCHAR(16) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CommerceSellers] PRIMARY KEY ([SellerId]),
    CONSTRAINT [FK_CommerceSellers_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_CommerceSellers_Parties] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId]),
    CONSTRAINT [UQ_CommerceSellers_Business_Code] UNIQUE ([BusinessId], [Code]),
    CONSTRAINT [UQ_CommerceSellers_Business_Party] UNIQUE ([BusinessId], [PartyId]),
    CONSTRAINT [CK_CommerceSellers_Commission] CHECK ([DefaultCommissionPercent] IS NULL OR [DefaultCommissionPercent] BETWEEN 0 AND 100),
    CONSTRAINT [CK_CommerceSellers_Basis] CHECK ([CommissionBasis] IN (N'SaleBeforeTax',N'SaleAfterTax',N'GrossMargin')),
    CONSTRAINT [CK_CommerceSellers_Trigger] CHECK ([CommissionTrigger] IN (N'Sale',N'Collection'))
);
GO
