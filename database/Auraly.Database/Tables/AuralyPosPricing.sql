CREATE TABLE [dbo].[PriceLists] (
    [PriceListId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PriceLists] PRIMARY KEY ([PriceListId]),
    CONSTRAINT [FK_PriceLists_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_PriceLists_Business_Code] UNIQUE ([BusinessId], [Code])
);
GO

CREATE TABLE [dbo].[PriceListItems] (
    [PriceListItemId] UNIQUEIDENTIFIER NOT NULL,
    [PriceListId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [MinimumQuantity] DECIMAL(19,6) NOT NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [CurrencyCode] CHAR(3) NOT NULL,
    [ValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [ValidUntil] DATETIMEOFFSET(7) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_PriceListItems] PRIMARY KEY ([PriceListItemId]),
    CONSTRAINT [FK_PriceListItems_PriceLists] FOREIGN KEY ([PriceListId]) REFERENCES [dbo].[PriceLists] ([PriceListId]),
    CONSTRAINT [FK_PriceListItems_Products] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProductId]),
    CONSTRAINT [CK_PriceListItems_Quantity] CHECK ([MinimumQuantity] > 0),
    CONSTRAINT [CK_PriceListItems_Amount] CHECK ([Amount] >= 0),
    CONSTRAINT [CK_PriceListItems_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [ValidFrom])
);
GO
CREATE UNIQUE INDEX [UX_PriceListItems_ActiveBreak]
    ON [dbo].[PriceListItems] ([PriceListId], [ProductId], [MinimumQuantity])
    WHERE [IsActive] = 1;
GO

CREATE TABLE [dbo].[ResolvedPriceChannelItems] (
    [ResolvedPriceChannelItemId] UNIQUEIDENTIFIER NOT NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
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
    CONSTRAINT [CK_ResolvedPriceChannelItems_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [ValidFrom])
);
GO
CREATE UNIQUE INDEX [UX_ResolvedPriceChannelItems_Active]
    ON [dbo].[ResolvedPriceChannelItems] ([PriceChannelId], [ProductId])
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

CREATE TABLE [dbo].[CommerceCustomers] (
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [IdentificationType] NVARCHAR(16) NOT NULL,
    [Identification] NVARCHAR(40) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Email] NVARCHAR(254) NULL,
    [Phone] NVARCHAR(32) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CommerceCustomers] PRIMARY KEY ([CustomerId]),
    CONSTRAINT [FK_CommerceCustomers_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_CommerceCustomers_Business_Identification] UNIQUE ([BusinessId], [IdentificationType], [Identification])
);
GO
CREATE INDEX [IX_CommerceCustomers_Business_Name]
    ON [dbo].[CommerceCustomers] ([BusinessId], [Name]);
GO

CREATE TABLE [dbo].[CustomerBusinessPricing] (
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [PriceListId] UNIQUEIDENTIFIER NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CustomerBusinessPricing] PRIMARY KEY ([CustomerId]),
    CONSTRAINT [FK_CustomerBusinessPricing_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[CommerceCustomers] ([CustomerId]),
    CONSTRAINT [FK_CustomerBusinessPricing_PriceLists] FOREIGN KEY ([PriceListId]) REFERENCES [dbo].[PriceLists] ([PriceListId]),
    CONSTRAINT [FK_CustomerBusinessPricing_PriceChannels] FOREIGN KEY ([PriceChannelId]) REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [CK_CustomerBusinessPricing_Exclusive] CHECK (
        NOT ([PriceListId] IS NOT NULL AND [PriceChannelId] IS NOT NULL))
);
GO

CREATE TABLE [dbo].[CommerceSellers] (
    [SellerId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [Name] NVARCHAR(160) NOT NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_CommerceSellers] PRIMARY KEY ([SellerId]),
    CONSTRAINT [FK_CommerceSellers_Businesses] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [UQ_CommerceSellers_Business_Code] UNIQUE ([BusinessId], [Code])
);
GO
