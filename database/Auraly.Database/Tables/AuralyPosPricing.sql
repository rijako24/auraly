CREATE TABLE [dbo].[ResolvedPriceChannelItems] (
    [ResolvedPriceChannelItemId] UNIQUEIDENTIFIER NOT NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [MinimumQuantity] DECIMAL(19,6) NOT NULL CONSTRAINT [DF_ResolvedPriceChannelItems_MinimumQuantity] DEFAULT (1),
    [Amount] DECIMAL(19,4) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [ValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [ValidUntil] DATETIMEOFFSET(7) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_ResolvedPriceChannelItems] PRIMARY KEY ([ResolvedPriceChannelItemId]),
    CONSTRAINT [FK_ResolvedPriceChannelItems_PriceChannels] FOREIGN KEY ([PriceChannelId]) REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [FK_ResolvedPriceChannelItems_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_ResolvedPriceChannelItems_Amount] CHECK ([Amount] >= 0),
    CONSTRAINT [CK_ResolvedPriceChannelItems_MinimumQuantity] CHECK ([MinimumQuantity] > 0),
    CONSTRAINT [CK_ResolvedPriceChannelItems_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [ValidFrom])
);
GO
CREATE UNIQUE INDEX [UX_ResolvedPriceChannelItems_Active]
    ON [dbo].[ResolvedPriceChannelItems] ([PriceChannelId], [ProductId], [MinimumQuantity])
    WHERE [IsActive] = 1;
GO

CREATE TABLE [dbo].[PriceChannelExclusions] (
    [PriceChannelId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PriceChannelExclusions] PRIMARY KEY ([PriceChannelId], [ProductId]),
    CONSTRAINT [FK_PriceChannelExclusions_PriceChannels] FOREIGN KEY ([PriceChannelId]) REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [FK_PriceChannelExclusions_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId])
);
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
