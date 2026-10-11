CREATE TABLE [dbo].[Businesses] (
    [BusinessId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(2000) NOT NULL,
    [Address] NVARCHAR(500) NOT NULL,
    [Phone] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [Website] NVARCHAR(500) NOT NULL,
    [TimeZone] NVARCHAR(100) NOT NULL CONSTRAINT [DF_Businesses_TimeZone] DEFAULT N'America/Bogota',
    [SharesProductPrices] BIT NOT NULL CONSTRAINT [DF_Businesses_SharesProductPrices] DEFAULT (0),
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    [PointOfSaleMode] NVARCHAR(32) NOT NULL CONSTRAINT [DF_Businesses_PointOfSaleMode] DEFAULT N'DirectSale',
    [RestaurantRequireWaiterCredential] BIT NOT NULL CONSTRAINT [DF_Businesses_RestaurantRequireWaiterCredential] DEFAULT (1),
    [RestaurantRestrictTableToWaiter] BIT NOT NULL CONSTRAINT [DF_Businesses_RestaurantRestrictTableToWaiter] DEFAULT (0),
    [RestaurantSuggestedTipPercent] DECIMAL(5,2) NOT NULL CONSTRAINT [DF_Businesses_RestaurantSuggestedTipPercent] DEFAULT (0),
    CONSTRAINT [FK_Businesses_Tenants] FOREIGN KEY ([TenantId])
        REFERENCES [dbo].[Tenants] ([TenantId])
        ON DELETE NO ACTION,
    CONSTRAINT [UQ_Businesses_Business_Tenant] UNIQUE ([BusinessId],[TenantId]),
    CONSTRAINT [CK_Businesses_PointOfSaleMode] CHECK ([PointOfSaleMode] COLLATE Latin1_General_100_BIN2 IN (N'DirectSale',N'RestaurantBar')),
    CONSTRAINT [CK_Businesses_RestaurantWaiterPolicy] CHECK ([RestaurantRestrictTableToWaiter]=0 OR [RestaurantRequireWaiterCredential]=1),
    CONSTRAINT [CK_Businesses_RestaurantSuggestedTipPercent] CHECK ([RestaurantSuggestedTipPercent]>=0 AND [RestaurantSuggestedTipPercent]<=10)
);

GO

CREATE INDEX [IX_Businesses_TenantId] ON [dbo].[Businesses] ([TenantId]);

GO
